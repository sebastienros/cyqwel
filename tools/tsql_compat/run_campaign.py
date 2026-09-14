"""Acquire pinned, inert test data and run the T-SQL compatibility campaign."""

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import urllib.request

import scriptdom_corpus
import sqlglot_corpus


HERE = Path(__file__).resolve().parent
REPOSITORY = HERE.parent.parent
EXTRACTORS = {"sqlglot": sqlglot_corpus.extract, "scriptdom": scriptdom_corpus.extract}
MAX_SOURCE_BYTES = 256 * 1024 * 1024


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def relative_path(value):
    path = PurePosixPath(value)
    if not value or path.is_absolute() or ".." in path.parts or "\\" in value:
        raise ValueError(f"Unsafe source path: {value!r}")
    return path


def extract_archive(archive, destination, source):
    includes = [relative_path(value) for value in source["include"]]
    total = 0
    for member in archive:
        path = relative_path(member.name)
        if not path.parts[0].endswith("-" + source["revision"]):
            raise ValueError(f"Archive does not match pinned revision: {member.name}")
        relative = PurePosixPath(*path.parts[1:])
        if not any(relative == prefix or prefix in relative.parents for prefix in includes):
            continue
        if member.isdir():
            continue
        if not member.isfile():
            raise ValueError(f"Not a regular source file: {member.name}")
        total += member.size
        if total > MAX_SOURCE_BYTES:
            raise ValueError("Source archive exceeds the extraction size budget.")
        target = destination.joinpath(*relative.parts)
        target.parent.mkdir(parents=True, exist_ok=True)
        if target.exists():
            raise ValueError(f"Duplicate archive entry: {member.name}")
        stream = archive.extractfile(member)
        if stream is None:
            raise ValueError(f"Cannot read archive entry: {member.name}")
        with stream, target.open("xb") as output:
            shutil.copyfileobj(stream, output)


def file_hashes(root):
    hashes = {}
    for path in sorted(root.rglob("*")):
        if path.is_symlink():
            raise ValueError(f"Source cache contains a symlink: {path}")
        if path.is_file() and path != root / ".source.json":
            hashes[path.relative_to(root).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    return hashes


def acquire_source(source, cache, offline=False):
    if source["source"] not in EXTRACTORS:
        raise ValueError(f"Unknown source: {source['source']}")
    if not re.fullmatch(r"[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+", source["repository"]):
        raise ValueError("Invalid GitHub repository.")
    if not re.fullmatch(r"[0-9a-f]{40}", source["revision"]):
        raise ValueError("Source revisions must be full immutable commit hashes.")
    relative_path(source["licensePath"])
    identity = {
        key: source[key]
        for key in ("source", "repository", "revision", "license", "licensePath", "include")
    }
    target = cache / f"{source['source']}-{source['revision']}"
    marker = target / ".source.json"
    if target.exists():
        if target.is_symlink() or marker.is_symlink() or not marker.is_file():
            raise ValueError(f"Incomplete source cache: {target}")
        saved = json.loads(marker.read_text(encoding="utf-8"))
        saved_identity = {key: saved["source"].get(key) for key in identity}
        if saved_identity != identity or saved["files"] != file_hashes(target):
            raise ValueError(f"Source cache changed; use a fresh output directory: {target}")
        return target
    if offline:
        raise ValueError(f"Pinned source is not cached: {target}")
    cache.mkdir(parents=True, exist_ok=True)
    url = f"https://codeload.github.com/{source['repository']}/tar.gz/{source['revision']}"
    print(f"Fetching {source['source']} at {source['revision']}", flush=True)
    with tempfile.TemporaryDirectory(prefix=f".{source['source']}-", dir=cache) as temporary:
        temporary = Path(temporary)
        archive_path = temporary / "source.tar.gz"
        request = urllib.request.Request(url, headers={"User-Agent": "Cyqwel-TSQL-campaign"})
        with urllib.request.urlopen(request, timeout=120) as response, archive_path.open("wb") as output:
            total = 0
            while chunk := response.read(1024 * 1024):
                total += len(chunk)
                if total > MAX_SOURCE_BYTES:
                    raise ValueError("Source download exceeds the size budget.")
                output.write(chunk)
        tree = temporary / "tree"
        tree.mkdir()
        with tarfile.open(archive_path, "r:gz") as archive:
            extract_archive(archive, tree, source)
        if not (tree / source["licensePath"]).is_file():
            raise ValueError(f"Source license was not acquired: {source['licensePath']}")
        write_json(tree / ".source.json", {"source": identity, "files": file_hashes(tree)})
        tree.rename(target)
    return target


def build_corpus(sources, cache, offline=False):
    cases, exclusions = [], []
    for source in sources:
        root = acquire_source(source, cache, offline)
        extracted, excluded = EXTRACTORS[source["source"]](root)
        if not extracted:
            raise ValueError(f"Extractor produced no candidates for {source['source']}.")
        for key, actual in (("expectedCandidates", len(extracted)), ("expectedExclusions", len(excluded))):
            if key in source and source[key] != actual:
                raise ValueError(
                    f"{source['source']} {key}: pinned {source[key]}, extracted {actual}; "
                    "review the extractor and denominator before updating the expectation."
                )
        cases.extend(extracted)
        exclusions.extend(excluded)
        print(
            f"{source['source']}: {len(extracted)} candidates, {len(excluded)} extraction exclusions",
            flush=True,
        )
    ids = [case["id"] for case in cases]
    if len(ids) != len(set(ids)):
        raise ValueError("Extractors produced duplicate case IDs.")
    return {"schemaVersion": 1, "sources": sources, "cases": cases, "exclusions": exclusions}


def summarize(report, output):
    sources = {source["source"]: source for source in report["sources"]}
    lines = [
        "# T-SQL compatibility campaign",
        "",
        f"Cyqwel: `{report['cyqwelRevision']}`. ScriptDom: `{report['scriptDomVersion']}`, "
        f"`{report['parserVersion']}`, initial quoted identifiers ON.",
        "",
        "Original SQL only; no SQL is executed. Input and statement rows overlap: do not add their totals.",
        "Statements are sliced from zero-error reference scripts, then independently revalidated. "
        "Reference-invalid, empty, and context-dependent inputs are not Cyqwel parsing gaps.",
        "",
        "| Source / scope | Candidates | Eligible | Cyqwel parsed | Parse rejected |",
        "| --- | ---: | ---: | ---: | ---: |",
    ]
    if report.get("caseFilter"):
        lines[4:4] = [f"Filtered run: `{report['caseFilter']}`; this is not a full-corpus result.", ""]
    for summary in report["summary"]:
        lines.append(
            f"| {summary['source']} / {summary['scope']} | {summary['candidates']} | "
            f"{summary['eligible']} | {summary['parsed']} | "
            f"{summary['outcomes'].get('parse-rejected', 0)} |"
        )
    lines += ["", "## Outcomes", "", "| Source / scope | Outcome | Count |", "| --- | --- | ---: |"]
    for summary in report["summary"]:
        for outcome, count in summary["outcomes"].items():
            lines.append(f"| {summary['source']} / {summary['scope']} | {outcome} | {count} |")
    lines += [
        "",
        "`reference-changed` is a review signal, not a verified semantic defect: it includes harmless "
        "quoting, case, parentheses, and other normalization. `round-trip-stable` does not prove full "
        "AST or semantic equivalence. The explicit AST audit currently checks aliases/assignments "
        "only in a single top-level SELECT query specification.",
        "",
        "## Statement-level parsing gaps",
        "",
        "| Source | ScriptDom statement kind | Occurrences |",
        "| --- | --- | ---: |",
    ]
    groups = Counter(
        (result["source"], result.get("statementType", "(unknown)"))
        for result in report["results"]
        if result["scope"] == "statement" and result["outcome"] == "parse-rejected"
    )
    for (source, kind), count in sorted(groups.items(), key=lambda pair: (-pair[1], pair[0])):
        lines.append(f"| {source} | {kind} | {count} |")
    lines += [
        "",
        "Counts are fixture occurrences, not unique features. Administrative and advanced DDL "
        "may be outside Cyqwel's documented AST; these are compatibility gaps, not promises of support.",
        "",
        "## Representative evidence",
        "",
        "The shortest fixture in each selected category is shown; these are not automatically minimized. "
        "`results.json` and `results.jsonl` preserve every input, location, diagnostic, and generated SQL.",
    ]
    evidence = {}
    for result in report["results"]:
        if result["scope"] != "statement" or result["outcome"] not in {
            "parse-rejected", "ast-mismatch", "generated-reference-rejected",
            "round-trip-changed", "generation-unsupported", "reparse-rejected", "exception",
        }:
            continue
        key = (result["outcome"], result.get("statementType", "(unknown)"))
        if key not in evidence or len(result["sql"]) < len(evidence[key]["sql"]):
            evidence[key] = result
    for key, result in sorted(evidence.items()):
        source = sources[result["source"]]
        url = (
            f"https://github.com/{source['repository']}/blob/{source['revision']}/"
            f"{result['path']}#L{result['sourceLine']}"
        )
        lines += [
            "",
            f"### {key[0]} / {key[1]}",
            "",
            f"[Upstream source]({url}) - `{result['id']}`",
            "",
            f"Original input (`--case`): `{result['caseId']}`",
            "",
        ]
        lines += ["    " + line for line in result["sql"].splitlines()]
        if result.get("parseError"):
            lines += ["", "Diagnostic: " + result["parseError"]["message"]]
        for mismatch in result.get("astMismatches", []):
            lines += [
                "",
                f"AST check `{mismatch['check']}`: expected `{mismatch['expected']}`, "
                f"actual `{mismatch['actual']}`.",
            ]
        if result.get("generatedSql"):
            lines += ["", "Generated:", ""]
            lines += ["    " + line for line in result["generatedSql"].splitlines()]
    lines += ["", "## Pinned sources", ""]
    for source in report["sources"]:
        lines.append(
            f"- [{source['repository']}](https://github.com/{source['repository']}/tree/"
            f"{source['revision']}) at `{source['revision']}` ({source['license']}). "
            f"The upstream `{source['licensePath']}` is preserved in the source cache."
        )
    lines += [
        "",
        f"Extraction exclusions: {len(report['extractionExclusions'])}. See the full "
        "`extractionExclusions` array for paths, locations, and reasons.",
        "",
    ]
    (output / "summary.md").write_text("\n".join(lines), encoding="utf-8")
    write_json(output / "summary.json", {
        key: report[key]
        for key in (
            "schemaVersion", "cyqwelRevision", "scriptDomVersion", "parserVersion",
            "quotedIdentifiers", "sources", "summary",
        )
    } | {
        "caseFilter": report.get("caseFilter"),
        "extractionExclusionCount": len(report["extractionExclusions"]),
    })


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, default=REPOSITORY / "artifacts/tsql-compat")
    parser.add_argument("--offline", action="store_true", help="Require the pinned source cache; never fetch.")
    parser.add_argument("--extract-only", action="store_true")
    parser.add_argument("--case", help="Evaluate one corpus case ID, including its statement slices.")
    args = parser.parse_args(argv)
    output = args.output_dir.resolve()
    output.mkdir(parents=True, exist_ok=True)
    sources = json.loads((HERE / "sources.json").read_text(encoding="utf-8"))["sources"]
    corpus = build_corpus(sources, output / "upstream", args.offline)
    corpus_path = output / "corpus.json"
    write_json(corpus_path, corpus)
    if args.extract_only:
        return 0
    revision = subprocess.check_output(
        ["git", "-C", str(REPOSITORY), "rev-parse", "HEAD"], text=True
    ).strip()
    source_changes = subprocess.check_output(
        ["git", "-C", str(REPOSITORY), "status", "--porcelain", "--", "src/Cyqwel"], text=True
    ).strip()
    if source_changes:
        revision += "+modified-source"
    command = [
        "dotnet", "run", "--project",
        str(REPOSITORY / "tools/Cyqwel.TSqlCompatibility/Cyqwel.TSqlCompatibility.csproj"),
        "--configuration", "Release", "--",
        "--corpus", str(corpus_path), "--output", str(output / "results.json"),
        "--revision", revision,
    ]
    if args.case:
        command += ["--case", args.case]
    result = subprocess.run(command, cwd=REPOSITORY, check=False)
    if result.returncode not in (0, 2):
        raise subprocess.CalledProcessError(result.returncode, command)
    summarize(json.loads((output / "results.json").read_text(encoding="utf-8")), output)
    return result.returncode


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, tarfile.TarError, subprocess.CalledProcessError) as error:
        print(f"Campaign failed: {error}", file=sys.stderr)
        sys.exit(1)
