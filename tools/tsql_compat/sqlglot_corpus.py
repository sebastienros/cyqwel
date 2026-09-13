"""Extract SQLGlot's T-SQL test inputs without importing or executing its code.

``extract(root)`` reads only ``tests/dialects/test_tsql.py`` beneath an upstream
repository root and returns ``(candidates, exclusions)``. Candidates are NOT
verified T-SQL: expression fragments, extensions, and command passthroughs stay
in the corpus for reference validation. Explicit command-warning/Command
assertions are marked in ``context``; unmarked inputs may also be passthroughs.

Helper semantics were inspected at SQLGlot commit
5cfb5997a99010940138670adf3d6b34ac5a0a08 (https://github.com/tobymao/sqlglot, MIT):
Validator parses validate_identity/validate_transpile's first argument and both
validate_all's first argument and its read values, but never its write values.
The latter, non-T-SQL reads, and expected-error cases are explicit exclusions,
not extraction failures or candidates. Direct parse/parse_one/transpile calls
must select T-SQL as their INPUT dialect. Unknown helpers (including a bare
``validate(True, ...)``) are excluded rather than guessing their semantics.

The bounded evaluator supports literal bindings, concatenation, scalar
f-strings, basic integer arithmetic, textwrap.dedent, string stripping, and
finite list/tuple/range/zip/enumerate loops. It never evaluates arbitrary Python,
SQL, generated SQLGlot expressions, comprehensions, or external fixture URLs.
Unsupported expressions/control flow produce exclusions at affected callsites.
Each loop iteration and helper input retains distinct, deterministic provenance.
"""

from __future__ import annotations

import ast
from collections import defaultdict
from dataclasses import dataclass, field
from pathlib import Path
import textwrap


_TEST_PATH = "tests/dialects/test_tsql.py"
_MAX_TEXT = 1_000_000
_MAX_ITEMS = 10_000
_MAX_DEPTH = 60
_MAX_ITERATIONS = 10_000
_HELPERS = {"validate_identity", "validate_all", "validate_transpile", "parse_one"}
_PARSERS = {"sqlglot.parse", "sqlglot.parse_one", "sqlglot.transpile"}
_BUILTINS = {"range", "zip", "enumerate"}


@dataclass(frozen=True)
class _Unknown:
    reason: str


@dataclass(frozen=True)
class _Symbol:
    name: str


@dataclass
class _Scope:
    names: dict[str, object]
    attributes: dict[str, object] = field(default_factory=dict)
    context: str = ""
    iterations: tuple[tuple[int, int], ...] = ()
    overrides: frozenset[str] = frozenset()

    def copy(self) -> _Scope:
        return _Scope(
            self.names.copy(), self.attributes.copy(), self.context,
            self.iterations, self.overrides,
        )


def _bounded(value: object) -> object:
    if isinstance(value, (str, bytes)) and len(value) > _MAX_TEXT:
        return _Unknown(f"literal text exceeds {_MAX_TEXT} characters")
    if isinstance(value, (list, tuple, dict, range)) and len(value) > _MAX_ITEMS:
        return _Unknown(f"literal collection exceeds {_MAX_ITEMS} items")
    if isinstance(value, int) and value.bit_length() > 64:
        return _Unknown("literal integer exceeds 64 bits")
    return value


def _evaluate(node: ast.AST | None, scope: _Scope, depth: int = 0) -> object:
    if node is None:
        return None
    if depth > _MAX_DEPTH:
        return _Unknown(f"literal expression exceeds depth {_MAX_DEPTH}")

    def value(child: ast.AST | None) -> object:
        return _evaluate(child, scope, depth + 1)

    if isinstance(node, ast.Constant):
        if type(node.value) in (str, int, float, bool, type(None)):
            return _bounded(node.value)
        return _Unknown(f"unsupported literal type: {type(node.value).__name__}")
    if isinstance(node, ast.Name):
        if node.id in scope.names:
            return scope.names[node.id]
        if node.id in _BUILTINS:
            return _Symbol(f"builtins.{node.id}")
        return _Unknown(f"dynamic Python name: {node.id}")
    if isinstance(node, ast.Attribute):
        if isinstance(node.value, ast.Name) and node.value.id == "self":
            return scope.attributes.get(node.attr, _Unknown(f"dynamic attribute: self.{node.attr}"))
        base = value(node.value)
        if isinstance(base, _Symbol):
            return _Symbol(f"{base.name}.{node.attr}")
        return _Unknown(f"dynamic Python attribute: {node.attr}")
    if isinstance(node, (ast.List, ast.Tuple)):
        if len(node.elts) > _MAX_ITEMS:
            return _Unknown(f"literal collection exceeds {_MAX_ITEMS} items")
        items = [value(item) for item in node.elts]
        return tuple(items) if isinstance(node, ast.Tuple) else items
    if isinstance(node, ast.Dict):
        if len(node.keys) > _MAX_ITEMS:
            return _Unknown(f"literal collection exceeds {_MAX_ITEMS} items")
        result = {}
        for key_node, item_node in zip(node.keys, node.values):
            if key_node is None:
                extra = value(item_node)
                if not isinstance(extra, dict):
                    return _Unknown("dynamic Python dictionary unpacking")
                result.update(extra)
            else:
                key = value(key_node)
                if type(key) not in (str, int, float, bool, type(None)):
                    return _Unknown("dynamic or unsupported Python dictionary key")
                result[key] = value(item_node)
            if len(result) > _MAX_ITEMS:
                return _Unknown(f"literal collection exceeds {_MAX_ITEMS} items")
        return result
    if isinstance(node, ast.JoinedStr):
        parts = []
        length = 0
        for item in node.values:
            if isinstance(item, ast.FormattedValue):
                part = value(item.value)
                if isinstance(part, _Unknown):
                    return part
                if type(part) not in (str, int, float, bool, type(None)):
                    return _Unknown("f-string interpolation is not a literal scalar")
                if item.format_spec is not None and value(item.format_spec) != "":
                    return _Unknown("nonempty f-string format specifications are not supported")
                if item.conversion == 114:
                    part = repr(part)
                elif item.conversion == 97:
                    part = ascii(part)
                elif item.conversion in (-1, 115):
                    part = str(part)
                else:
                    return _Unknown("unsupported f-string conversion")
            else:
                part = value(item)
            if not isinstance(part, str):
                return part if isinstance(part, _Unknown) else _Unknown("nonliteral f-string text")
            length += len(part)
            if length > _MAX_TEXT:
                return _Unknown(f"interpolated text exceeds {_MAX_TEXT} characters")
            parts.append(part)
        return "".join(parts)
    if isinstance(node, ast.BinOp):
        left, right = value(node.left), value(node.right)
        if isinstance(left, _Unknown):
            return left
        if isinstance(right, _Unknown):
            return right
        if isinstance(node.op, ast.Add):
            if type(left) is type(right) and isinstance(left, (str, list, tuple)):
                limit = _MAX_TEXT if isinstance(left, str) else _MAX_ITEMS
                if len(left) + len(right) > limit:
                    return _Unknown(f"literal concatenation exceeds limit {limit}")
                return left + right
            if type(left) is int and type(right) is int:
                return _bounded(left + right)
        if isinstance(node.op, ast.Sub) and type(left) is int and type(right) is int:
            return _bounded(left - right)
        return _Unknown(f"unsupported literal operator: {type(node.op).__name__}")
    if isinstance(node, ast.UnaryOp):
        operand = value(node.operand)
        if isinstance(operand, _Unknown):
            return operand
        if type(operand) is int and isinstance(node.op, (ast.USub, ast.UAdd)):
            return _bounded(-operand if isinstance(node.op, ast.USub) else operand)
        if isinstance(node.op, ast.Not) and not isinstance(operand, _Symbol):
            return not operand
    if isinstance(node, ast.IfExp):
        condition = value(node.test)
        if isinstance(condition, (_Unknown, _Symbol)):
            return _Unknown("dynamic Python conditional expression")
        return value(node.body if condition else node.orelse)
    if isinstance(node, ast.Subscript):
        container, key = value(node.value), value(node.slice)
        if isinstance(container, _Unknown):
            return container
        if isinstance(key, _Unknown):
            return key
        if isinstance(container, (str, list, tuple)) and type(key) is int:
            try:
                return container[key]
            except IndexError:
                return _Unknown("literal subscript is out of range")
        if isinstance(container, dict) and type(key) in (str, int, float, bool, type(None)):
            return container.get(key, _Unknown("literal dictionary key is missing"))
        return _Unknown("dynamic or unsupported Python subscript")
    if isinstance(node, ast.Call):
        if node.keywords or any(isinstance(arg, ast.Starred) for arg in node.args):
            return _Unknown("literal function call uses keyword or starred arguments")
        args = [value(arg) for arg in node.args]
        unknown = next((arg for arg in args if isinstance(arg, _Unknown)), None)
        if unknown is not None:
            return unknown
        function = value(node.func)
        if isinstance(function, _Symbol):
            if function.name == "textwrap.dedent" and len(args) == 1 and isinstance(args[0], str):
                return _bounded(textwrap.dedent(args[0]))
            if function.name == "builtins.range" and 1 <= len(args) <= 3:
                if not all(type(arg) is int for arg in args):
                    return _Unknown("range arguments are not literal integers")
                try:
                    return _bounded(range(*args))
                except (ValueError, OverflowError) as error:
                    return _Unknown(f"invalid literal range: {error}")
            if function.name == "builtins.zip" and args:
                if all(isinstance(arg, (list, tuple, range)) for arg in args):
                    return _bounded(list(zip(*args)))
            if function.name == "builtins.enumerate" and 1 <= len(args) <= 2:
                if isinstance(args[0], (list, tuple, range)):
                    start = args[1] if len(args) == 2 else 0
                    if type(start) is int:
                        return _bounded(list(enumerate(args[0], start)))
        if isinstance(node.func, ast.Attribute) and node.func.attr in {"strip", "lstrip", "rstrip"}:
            text = value(node.func.value)
            if isinstance(text, str) and len(args) <= 1:
                if not args or args[0] is None or isinstance(args[0], str):
                    if node.func.attr == "strip":
                        return text.strip(*args)
                    if node.func.attr == "lstrip":
                        return text.lstrip(*args)
                    return text.rstrip(*args)
        return _Unknown("dynamic Python function call")
    return _Unknown(f"dynamic or unsupported Python expression: {type(node).__name__}")


def _bind(target: ast.AST, value: object, scope: _Scope) -> None:
    if isinstance(target, ast.Name):
        scope.names[target.id] = value
    elif isinstance(target, ast.Attribute):
        if isinstance(target.value, ast.Name) and target.value.id == "self":
            scope.attributes[target.attr] = value
    elif isinstance(target, (ast.Tuple, ast.List)):
        if isinstance(value, (list, tuple)) and len(target.elts) == len(value):
            for child, item in zip(target.elts, value):
                _bind(child, item, scope)
        else:
            unknown = value if isinstance(value, _Unknown) else _Unknown("unsupported tuple unpacking")
            for child in target.elts:
                _bind(child, unknown, scope)
    elif isinstance(target, ast.Starred):
        _bind(target.value, _Unknown("starred assignment is not supported"), scope)


def _import(node: ast.Import | ast.ImportFrom, scope: _Scope) -> None:
    for alias in node.names:
        if isinstance(node, ast.ImportFrom):
            scope.names[alias.asname or alias.name] = _Symbol(f"{node.module}.{alias.name}")
        else:
            name = alias.asname or alias.name.split(".")[0]
            scope.names[name] = _Symbol(alias.name if alias.asname else name)


def _argument(call: ast.Call, index: int, name: str) -> ast.AST | None:
    for keyword in call.keywords:
        if keyword.arg == name:
            return keyword.value
    return call.args[index] if index < len(call.args) else None


def _is_self(node: ast.AST, names: set[str]) -> bool:
    return (
        isinstance(node, ast.Attribute)
        and isinstance(node.value, ast.Name)
        and node.value.id == "self"
        and node.attr in names
    )


class _LocalNames(ast.NodeVisitor):
    def __init__(self):
        self.names: set[str] = set()

    def visit_Name(self, node: ast.Name) -> None:
        if isinstance(node.ctx, (ast.Store, ast.Del)):
            self.names.add(node.id)

    def visit_FunctionDef(
        self, node: ast.FunctionDef | ast.AsyncFunctionDef | ast.ClassDef,
    ) -> None:
        self.names.add(node.name)

    visit_AsyncFunctionDef = visit_FunctionDef
    visit_ClassDef = visit_FunctionDef

    def visit_Import(self, node: ast.Import) -> None:
        self.names.update(alias.asname or alias.name.split(".")[0] for alias in node.names)

    def visit_ImportFrom(self, node: ast.ImportFrom) -> None:
        self.names.update(alias.asname or alias.name for alias in node.names)

    def visit_Lambda(self, node: ast.AST) -> None:
        pass

    visit_ListComp = visit_Lambda
    visit_SetComp = visit_Lambda
    visit_DictComp = visit_Lambda
    visit_GeneratorExp = visit_Lambda


def _has_exit(node: ast.AST, kinds: tuple[type[ast.AST], ...]) -> bool:
    if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef, ast.Lambda)):
        return False
    return isinstance(node, kinds) or any(_has_exit(child, kinds) for child in ast.iter_child_nodes(node))


def _contains_identity(container: object, target: object, depth: int = 0) -> bool:
    if container is target or depth > _MAX_DEPTH:
        return True
    if isinstance(container, dict):
        return any(_contains_identity(value, target, depth + 1) for value in container.values())
    if isinstance(container, (list, tuple)):
        return any(_contains_identity(value, target, depth + 1) for value in container)
    return False


class _Extractor:
    def __init__(self, tree: ast.Module):
        self.tree = tree
        self.candidates: list[dict] = []
        self.exclusions: list[dict] = []
        self.occurrences: dict[tuple, int] = defaultdict(int)
        self.iterations_left = _MAX_ITERATIONS
        self.mutated_containers: list[object] = []
        self.command_calls: set[ast.Call] = set()
        for node in ast.walk(tree):
            if not isinstance(node, ast.Call) or not isinstance(node.func, ast.Attribute):
                continue
            if node.func.attr == "assert_is" and node.args:
                asserted, expression = node.args[0], node.func.value
            elif node.func.attr == "assertIsInstance" and len(node.args) >= 2:
                expression, asserted = node.args[:2]
            else:
                continue
            if isinstance(asserted, ast.Attribute) and asserted.attr == "Command":
                if isinstance(expression, ast.Call):
                    self.command_calls.add(expression)

    def _context(self, scope: _Scope, slot: str) -> str:
        loops = "".join(f"/for@{line}[{index}]" for line, index in scope.iterations)
        return f"{scope.context}/{slot}{loops}"

    def _exclude(self, node: ast.AST, scope: _Scope, slot: str, reason: str) -> None:
        self.exclusions.append({
            "source": "sqlglot", "path": _TEST_PATH, "line": node.lineno,
            "context": self._context(scope, slot), "reason": reason,
        })

    def _input(
        self, call: ast.Call, scope: _Scope, slot: str, sql: object,
        dialect: object, blocked: str | None, command: bool = False,
    ) -> None:
        if blocked:
            self._exclude(call, scope, slot, blocked)
        elif isinstance(dialect, _Unknown):
            self._exclude(call, scope, slot, f"dynamic input dialect: {dialect.reason}")
        elif not isinstance(dialect, str) or dialect.split(",", 1)[0].strip() != "tsql":
            label = dialect.name if isinstance(dialect, _Symbol) else repr(dialect)
            self._exclude(call, scope, slot, f"non-TSQL input dialect: {label}")
        elif isinstance(sql, _Unknown):
            self._exclude(call, scope, slot, sql.reason)
        elif not isinstance(sql, str):
            self._exclude(call, scope, slot, f"input is not a literal SQL string: {type(sql).__name__}")
        else:
            key = (call.lineno, call.col_offset, slot)
            occurrence = self.occurrences[key]
            self.occurrences[key] += 1
            category = slot + ("/command-passthrough" if command else "")
            self.candidates.append({
                "id": f"sqlglot:{_TEST_PATH}:{call.lineno}:{call.col_offset}:{slot}:{occurrence}",
                "source": "sqlglot", "path": _TEST_PATH, "line": call.lineno,
                "sql": sql, "context": self._context(scope, category),
            })

    def _kind(self, call: ast.Call, scope: _Scope) -> str | None:
        if _is_self(call.func, _HELPERS):
            if call.func.attr in scope.overrides or call.func.attr in scope.attributes:
                return "unknown-helper"
            return call.func.attr
        function = _evaluate(call.func, scope)
        if isinstance(function, _Symbol) and function.name in _PARSERS:
            return function.name
        if isinstance(call.func, ast.Name) and call.func.id.startswith("validate"):
            return "unknown-helper"
        if isinstance(call.func, ast.Attribute):
            if _is_self(call.func, {call.func.attr}) and call.func.attr.startswith("validate"):
                return "unknown-helper"
        if isinstance(call.func, ast.Name) and call.func.id in {"parse_one", "parse", "transpile"}:
            return "unknown-helper"
        return None

    def _outputs(self, call: ast.Call, scope: _Scope, node: ast.AST | None, slot: str) -> None:
        if node is None:
            return
        outputs = _evaluate(node, scope)
        if outputs is None:
            return
        if isinstance(outputs, dict):
            for dialect, output in outputs.items():
                reason = "generated output is not a parser input"
                if isinstance(output, _Symbol) and output.name.endswith(".UnsupportedError"):
                    reason = "expected generation error is not a parser input"
                self._exclude(call, scope, f"{slot}[{dialect!r}]", reason)
        else:
            self._exclude(call, scope, slot, "generated output is not a parser input")

    def _call(self, call: ast.Call, scope: _Scope, blocked: str | None) -> bool:
        kind = self._kind(call, scope)
        if kind is None:
            return False
        if kind == "unknown-helper":
            self._exclude(call, scope, kind, blocked or "unknown helper semantics; not assumed positive")
            return True
        if any(isinstance(arg, ast.Starred) for arg in call.args) or any(
            keyword.arg is None for keyword in call.keywords
        ):
            self._exclude(call, scope, kind, blocked or "starred helper arguments require runtime binding")
            return True
        sql_node = _argument(call, 0, "sql")
        sql = _evaluate(sql_node, scope) if sql_node is not None else _Unknown("missing SQL argument")
        if kind in _HELPERS:
            dialect = scope.attributes.get("dialect", _Unknown("self.dialect is not statically known"))
        else:
            read = _evaluate(_argument(call, 1, "read"), scope)
            if kind == "sqlglot.transpile" or isinstance(read, (_Unknown, _Symbol)) or read:
                dialect = read
            else:
                dialect = _evaluate(_argument(call, 2, "dialect"), scope)
        command = call in self.command_calls or (
            kind == "validate_identity"
            and _evaluate(_argument(call, 3, "check_command_warning"), scope) is True
        )
        self._input(call, scope, f"{kind}.sql", sql, dialect, blocked, command)
        if kind == "validate_all":
            read_node = _argument(call, 1, "read")
            read = _evaluate(read_node, scope)
            if isinstance(read, dict):
                for read_dialect, read_sql in read.items():
                    self._input(
                        call, scope, f"{kind}.read[{read_dialect!r}]",
                        read_sql, read_dialect, blocked,
                    )
            elif read is not None:
                reason = read.reason if isinstance(read, _Unknown) else "read is not a literal dialect mapping"
                self._exclude(call, scope, f"{kind}.read", blocked or reason)
            self._outputs(call, scope, _argument(call, 2, "write"), f"{kind}.write")
        elif kind in {"validate_identity", "validate_transpile"}:
            output = _argument(call, 1, "write_sql")
            if output is not None:
                self._exclude(call, scope, f"{kind}.write_sql", "generated output is not a parser input")
        return True

    def _blocked(self, node: ast.AST, scope: _Scope, reason: str) -> None:
        for child in ast.walk(node):
            if isinstance(child, ast.Call):
                self._call(child, scope, reason)

    def _invalidate(self, node: ast.AST, scope: _Scope, reason: str) -> None:
        for child in ast.walk(node):
            if isinstance(child, (ast.Name, ast.Attribute)) and isinstance(child.ctx, (ast.Store, ast.Del)):
                _bind(child, _Unknown(reason), scope)
            if isinstance(child, ast.Subscript) and isinstance(child.ctx, (ast.Store, ast.Del)):
                self._invalidate_container(child.value, scope, reason)

    def _invalidate_container(self, node: ast.AST, scope: _Scope, reason: str) -> None:
        original = _evaluate(node, scope)
        if isinstance(original, (list, dict)):
            self.mutated_containers.append(original)
            for values in (scope.names, scope.attributes):
                for name, value in list(values.items()):
                    if _contains_identity(value, original):
                        values[name] = _Unknown(reason)

    def _expression(self, node: ast.AST | None, scope: _Scope, blocked: str | None = None) -> None:
        if node is None:
            return
        if isinstance(node, (ast.Lambda, ast.ListComp, ast.SetComp, ast.DictComp, ast.GeneratorExp)):
            self._blocked(node, scope, blocked or "deferred/comprehension execution is not statically modeled")
            return
        if isinstance(node, (ast.IfExp, ast.BoolOp, ast.NamedExpr)):
            self._blocked(node, scope, blocked or "conditional expression execution is not statically modeled")
            if isinstance(node, ast.NamedExpr):
                self._invalidate(node, scope, "dynamic assignment expression")
            return
        if isinstance(node, ast.Call):
            if _is_self(node.func, {"assertRaises", "assertRaisesRegex"}):
                callable_index = 2 if node.func.attr == "assertRaisesRegex" else 1
                if len(node.args) > callable_index:
                    reason = "expected exception assertion is not a positive parser test"
                    nested = any(
                        isinstance(child, ast.Call) and self._kind(child, scope) is not None
                        for child in ast.walk(node) if child is not node
                    )
                    if not nested:
                        self._exclude(node, scope, "expected-error", reason)
                    self._blocked(node, scope, reason)
                    return
            recognized = self._call(node, scope, blocked)
            for child in ast.iter_child_nodes(node):
                self._expression(child, scope, blocked)
            pure = (
                isinstance(node.func, ast.Attribute)
                and _is_self(node.func, {node.func.attr})
                and (node.func.attr.startswith("assert") or node.func.attr == "subTest")
            )
            function = _evaluate(node.func, scope)
            if isinstance(function, _Symbol):
                pure = pure or function.name in {
                    "builtins.range", "builtins.zip", "builtins.enumerate", "textwrap.dedent",
                }
            if not recognized and not pure:
                reason = f"possible dynamic container mutation at line {node.lineno}"
                if isinstance(node.func, ast.Attribute):
                    self._invalidate_container(node.func.value, scope, reason)
                for arg in node.args:
                    self._invalidate_container(arg, scope, reason)
            return
        for child in ast.iter_child_nodes(node):
            self._expression(child, scope, blocked)

    def _block(self, statements: list[ast.stmt], scope: _Scope, blocked: str | None = None) -> bool:
        for index, statement in enumerate(statements):
            leaves_scope = False
            if isinstance(statement, (ast.Import, ast.ImportFrom)):
                _import(statement, scope)
            elif isinstance(statement, (ast.Assign, ast.AnnAssign)):
                self._expression(statement.value, scope, blocked)
                value = _Unknown(blocked) if blocked else _evaluate(statement.value, scope)
                targets = statement.targets if isinstance(statement, ast.Assign) else [statement.target]
                for target in targets:
                    if isinstance(target, ast.Subscript):
                        self._invalidate_container(target.value, scope, "dynamic subscript assignment")
                    else:
                        _bind(target, value, scope)
            elif isinstance(statement, ast.For):
                self._expression(statement.iter, scope, blocked)
                values = _evaluate(statement.iter, scope)
                reason = blocked
                empty = isinstance(values, (list, tuple, range)) and not values
                exits = any(
                    _has_exit(child, (ast.Return, ast.Raise, ast.Break, ast.Continue))
                    for child in statement.body
                )
                if not isinstance(values, (list, tuple, range)):
                    detail = values.reason if isinstance(values, _Unknown) else "not a literal sequence"
                    reason = reason or f"dynamic loop iterable: {detail}"
                elif len(values) > self.iterations_left:
                    reason = reason or f"static loop expansion exceeds {_MAX_ITERATIONS} iterations"
                elif empty:
                    reason = reason or "empty static loop has no input occurrences"
                elif exits:
                    reason = reason or "loop control-flow exits are not statically modeled"
                if reason:
                    child_scope = scope.copy()
                    _bind(statement.target, _Unknown(reason), child_scope)
                    for child in statement.body:
                        self._blocked(child, child_scope, reason)
                    if not empty:
                        self._invalidate(statement, scope, reason)
                else:
                    previous = scope.iterations
                    for iteration, item in enumerate(values):
                        scope.iterations = previous + ((statement.lineno, iteration),)
                        if self.iterations_left <= 0:
                            reason = f"static loop expansion exceeds {_MAX_ITERATIONS} iterations"
                            for child in statement.body:
                                self._blocked(child, scope, reason)
                            self._invalidate(statement, scope, reason)
                            break
                        self.iterations_left -= 1
                        _bind(statement.target, item, scope)
                        self._block(statement.body, scope)
                        if any(_contains_identity(values, item) for item in self.mutated_containers):
                            reason = "loop iterable may have been dynamically mutated; remaining iterations not expanded"
                            scope.iterations = previous + ((statement.lineno, iteration + 1),)
                            for child in statement.body:
                                self._blocked(child, scope, reason)
                            self._invalidate(statement, scope, reason)
                            break
                    scope.iterations = previous
                leaves_scope = self._block(statement.orelse, scope, reason if not empty else blocked)
                if not empty:
                    leaves_scope = leaves_scope or any(
                        _has_exit(child, (ast.Return, ast.Raise)) for child in statement.body
                    )
            elif isinstance(statement, ast.With):
                reason = blocked
                for item in statement.items:
                    manager = item.context_expr
                    self._expression(manager, scope, blocked)
                    if isinstance(manager, ast.Call) and _is_self(
                        manager.func, {"assertRaises", "assertRaisesRegex"}
                    ):
                        reason = reason or "expected exception assertion is not a positive parser test"
                    elif not (isinstance(manager, ast.Call) and _is_self(manager.func, {"subTest"})):
                        reason = reason or "dynamic context manager"
                    if item.optional_vars is not None:
                        _bind(item.optional_vars, _Unknown("context-manager result"), scope)
                leaves_scope = self._block(statement.body, scope, reason)
            elif isinstance(statement, ast.If):
                self._expression(statement.test, scope, blocked)
                condition = _evaluate(statement.test, scope)
                if blocked or isinstance(condition, (_Unknown, _Symbol)):
                    reason = blocked or "dynamic Python branch"
                    for child in statement.body + statement.orelse:
                        self._blocked(child, scope, reason)
                    self._invalidate(statement, scope, reason)
                    leaves_scope = _has_exit(statement, (ast.Return, ast.Raise, ast.Break, ast.Continue))
                else:
                    selected, skipped = (
                        (statement.body, statement.orelse) if condition
                        else (statement.orelse, statement.body)
                    )
                    leaves_scope = self._block(selected, scope)
                    for child in skipped:
                        self._blocked(child, scope.copy(), "statically unreachable branch")
            elif isinstance(statement, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
                scope.names[statement.name] = _Unknown("local helper body is not executed")
            elif isinstance(statement, (ast.Expr, ast.Assert)):
                self._expression(statement.value if isinstance(statement, ast.Expr) else statement.test, scope, blocked)
                if isinstance(statement, ast.Assert):
                    self._expression(statement.msg, scope, blocked)
            elif isinstance(statement, (ast.Return, ast.Raise, ast.Break, ast.Continue)):
                self._expression(statement, scope, blocked)
                leaves_scope = True
            else:
                reason = blocked or f"unsupported Python statement: {type(statement).__name__}"
                self._blocked(statement, scope, reason)
                self._invalidate(statement, scope, reason)
                leaves_scope = _has_exit(statement, (ast.Return, ast.Raise))
            if leaves_scope:
                for child in statements[index + 1:]:
                    self._blocked(child, scope, "control-flow exit; later inputs are not assumed reachable")
                return True
        return False

    def _test(
        self, method: ast.FunctionDef | ast.AsyncFunctionDef,
        scope: _Scope, blocked: str | None = None,
    ) -> None:
        local_scope = scope.copy()
        local_scope.context = f"{scope.context}.{method.name}" if scope.context else method.name
        bindings = _LocalNames()
        for statement in method.body:
            bindings.visit(statement)
        for name in bindings.names:
            local_scope.names[name] = _Unknown("local binding is not statically assigned yet")
        for arg in method.args.posonlyargs + method.args.args + method.args.kwonlyargs:
            local_scope.names[arg.arg] = _Unknown(f"runtime test parameter: {arg.arg}")
        if method.decorator_list:
            blocked = blocked or "decorated test execution is not statically modeled"
        if isinstance(method, ast.AsyncFunctionDef):
            blocked = blocked or "async test execution is not statically modeled"
        if any(isinstance(node, (ast.Global, ast.Nonlocal)) for node in ast.walk(method)):
            blocked = blocked or "global/nonlocal test state is not statically modeled"
        self._block(method.body, local_scope, blocked)

    def run(self) -> tuple[list[dict], list[dict]]:
        globals_scope = _Scope({})
        tests = []
        for node in self.tree.body:
            if isinstance(node, (ast.Import, ast.ImportFrom)):
                _import(node, globals_scope)
            elif isinstance(node, (ast.Assign, ast.AnnAssign)):
                targets = node.targets if isinstance(node, ast.Assign) else [node.target]
                for target in targets:
                    _bind(target, _evaluate(node.value, globals_scope), globals_scope)
            elif isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
                globals_scope.names[node.name] = _Unknown("module helper body is not executed")
                if node.name.startswith("test_"):
                    tests.append((node, {}, "", frozenset(), None))
            elif isinstance(node, ast.ClassDef):
                attributes = _Scope(globals_scope.names.copy())
                attribute_names = _LocalNames()
                for member in node.body:
                    if isinstance(member, (ast.Assign, ast.AnnAssign)):
                        attribute_names.visit(member)
                        targets = member.targets if isinstance(member, ast.Assign) else [member.target]
                        for target in targets:
                            _bind(target, _evaluate(member.value, attributes), attributes)
                overrides = frozenset(
                    member.name for member in node.body
                    if isinstance(member, (ast.FunctionDef, ast.AsyncFunctionDef))
                    and member.name in _HELPERS
                )
                class_values = {
                    name: attributes.names[name] for name in attribute_names.names
                    if name in attributes.names
                }
                for member in node.body:
                    if isinstance(member, (ast.FunctionDef, ast.AsyncFunctionDef)) and member.name.startswith("test_"):
                        reason = "decorated test class is not statically modeled" if node.decorator_list else None
                        tests.append((member, class_values, node.name, overrides, reason))
                globals_scope.names[node.name] = _Unknown("class object is not statically evaluated")
        for method, attributes, context, overrides, reason in tests:
            scope = _Scope(globals_scope.names.copy(), attributes, context, overrides=overrides)
            self._test(method, scope, reason)
        return self.candidates, self.exclusions


def extract(root: Path) -> tuple[list[dict], list[dict]]:
    """Return unverified input occurrences and explicit exclusions from a source tree.

    IDs are based on repository-relative path, call coordinates, argument role,
    and occurrence, never the absolute checkout path or normalized SQL. Missing
    files, invalid encoding, and invalid Python syntax raise rather than returning
    an apparently successful empty corpus.
    """
    path = root / _TEST_PATH
    tree = ast.parse(path.read_text(encoding="utf-8"), filename=_TEST_PATH)
    return _Extractor(tree).run()
