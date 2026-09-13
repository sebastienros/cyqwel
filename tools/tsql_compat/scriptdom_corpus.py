"""Extract original SQL fixtures from a Microsoft SqlScriptDOM source tree.

The layout was checked at c122a7f9e69e9e98b0abbe7b69693ce23aaca829
of https://github.com/microsoft/SqlScriptDOM (MIT license).
UTSqlScriptDom.csproj embeds TestScripts and PhaseOneTestScripts as inputs.
ParserTestOutput.cs uses the sibling Baselines* directories as generated-SQL
comparison outputs, not additional original inputs.

Candidates are whole fixtures, not statements or assertions of valid SQL.
Keep error, lexer, phase-one/recovery, empty, and version-specific inputs.
Inline C# test strings are outside this extractor's scope. Context describes
the fixture category; parser version and quoted-identifier settings must not
be inferred from filenames.
"""

import codecs
import os
from pathlib import Path
import re


_TEST_ROOT = Path("Test/SqlDom")
_INPUT_CONTEXTS = {
    "TestScripts": (
        "TestScripts: original parser/lexer input; may expect errors or require "
        "a particular SQL version, engine flavor, or quoted-identifier mode"
    ),
    "PhaseOneTestScripts": (
        "PhaseOneTestScripts: phase-one/recovery parser input; may contain "
        "intentional invalid prefixes or incomplete statements"
    ),
}
_BASELINE_DIRECTORY = re.compile(r"Baselines(?:[0-9]+|Common|FabricDW)")


def _decode_sql(data: bytes) -> str:
    """Consume an encoding BOM, but preserve all SQL characters and newlines."""
    if data.startswith((codecs.BOM_UTF32_LE, codecs.BOM_UTF32_BE)):
        raise UnicodeError("UTF-32 BOM is not a supported UTF-8/UTF-16 fixture")
    if data.startswith(codecs.BOM_UTF8):
        return data.decode("utf-8-sig")
    if data.startswith((codecs.BOM_UTF16_LE, codecs.BOM_UTF16_BE)):
        return data.decode("utf-16")

    # BOM-less UTF-16 SQL has a strong alternating NUL-byte signature in its
    # ASCII syntax. Do not guess a legacy code page when strict decoding fails.
    sample = data[:4096]
    sample = sample[: len(sample) // 2 * 2]
    pairs = len(sample) // 2
    even_nuls = sample[0::2].count(0)
    odd_nuls = sample[1::2].count(0)
    if odd_nuls * 2 >= pairs and odd_nuls > even_nuls * 4:
        return data.decode("utf-16-le")
    if even_nuls * 2 >= pairs and even_nuls > odd_nuls * 4:
        return data.decode("utf-16-be")
    return data.decode("utf-8")


def _raise_walk_error(error: OSError) -> None:
    raise error


def extract(root: Path) -> tuple[list[dict], list[dict]]:
    """Return candidates and explicit exclusions, each sorted by relative path.

    ``root`` is the repository root of a checkout or extracted source archive.
    Missing/unreadable test directories raise an OSError; individual read or
    decode failures are exclusions. Unrecognized SQL locations under
    Test/SqlDom are also reported rather than silently disappearing.

    Reads are strict UTF-8 or UTF-16, with or without a BOM. Only BOM-less
    UTF-16 with a strong alternating NUL-byte signature is auto-detected.
    Apart from consuming the initial encoding BOM, decoded text is unchanged:
    no trimming, newline conversion, GO removal, splitting, or regeneration.
    """
    test_root = root / _TEST_ROOT
    paths = []
    for directory, directory_names, file_names in os.walk(
        test_root, onerror=_raise_walk_error, followlinks=False
    ):
        directory = Path(directory)
        paths.extend(
            directory / name
            for name in file_names
            if Path(name).suffix.lower() == ".sql"
        )
        paths.extend(
            directory / name
            for name in directory_names
            if (directory / name).is_symlink()
        )

    candidates = []
    exclusions = []
    for path in sorted(paths, key=lambda path: path.relative_to(root).as_posix()):
        relative_path = path.relative_to(root).as_posix()
        parts = path.relative_to(test_root).parts
        category = parts[0] if len(parts) > 1 else ""
        context = _INPUT_CONTEXTS.get(
            category, f"{_TEST_ROOT.as_posix()}/{category}".rstrip("/")
        )
        provenance = {
            "source": "scriptdom",
            "path": relative_path,
            "line": 1,
            "context": context,
        }
        if path.is_symlink():
            exclusions.append({
                **provenance,
                "reason": (
                    "symbolic link excluded; linked fixture content is not "
                    "read or traversed"
                ),
            })
            continue
        if _BASELINE_DIRECTORY.fullmatch(category):
            exclusions.append({
                **provenance,
                "reason": (
                    "generated SQL comparison baseline, not an original parser "
                    "input (ParserTestOutput.VerifyResult)"
                ),
            })
            continue
        if category not in _INPUT_CONTEXTS:
            exclusions.append({
                **provenance,
                "reason": (
                    "SQL file outside the registered TestScripts and "
                    "PhaseOneTestScripts input directories; parser-input role "
                    "not established by UTSqlScriptDom.csproj"
                ),
            })
            continue
        try:
            sql = _decode_sql(path.read_bytes())
        except UnicodeError as error:
            exclusions.append({**provenance, "reason": f"decode failure: {error}"})
            continue
        except OSError as error:
            exclusions.append({
                **provenance,
                "reason": (
                    f"read failure ({type(error).__name__}): "
                    f"{error.strerror or str(error)}"
                ),
            })
            continue
        candidates.append({
            "id": f"scriptdom:{relative_path}",
            "source": "scriptdom",
            "path": relative_path,
            "line": 1,
            "sql": sql,
            "context": context,
        })
    return candidates, exclusions
