#!/usr/bin/env python3
"""
Parallel Log Error Analyzer

Usage:
    python log_analyzer.py path/to/logfile.txt
    python log_analyzer.py path/to/logfile.txt --mode sequential
    python log_analyzer.py path/to/logfile.txt --mode parallel --workers 4
    python log_analyzer.py path/to/logfile.txt --mode both
"""

from __future__ import annotations

import argparse
import re
import time
from collections import Counter
from concurrent.futures import ProcessPoolExecutor
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, List, Tuple


ERROR_RE = re.compile(r"\berror\b[:\-\s]*(.*)", re.IGNORECASE)
WARNING_RE = re.compile(r"\bwarning\b[:\-\s]*(.*)", re.IGNORECASE)


@dataclass
class AnalysisResult:
    total_lines: int = 0
    error_count: int = 0
    warning_count: int = 0
    error_messages: Counter[str] = None
    warning_messages: Counter[str] = None

    def __post_init__(self) -> None:
        if self.error_messages is None:
            self.error_messages = Counter()
        if self.warning_messages is None:
            self.warning_messages = Counter()

    def merge(self, other: "AnalysisResult") -> None:
        self.total_lines += other.total_lines
        self.error_count += other.error_count
        self.warning_count += other.warning_count
        self.error_messages.update(other.error_messages)
        self.warning_messages.update(other.warning_messages)


def normalize_message(text: str) -> str:
    text = text.strip()
    text = re.sub(r"^\[.*?\]\s*", "", text)   # remove leading [timestamp]-style tags
    text = re.sub(r"^\d{4}-\d{2}-\d{2}.*?\s+", "", text)  # remove date prefix if any
    return text.lower().strip() or "unknown message"


def analyze_line(line: str) -> Tuple[int, int, Counter[str], Counter[str]]:
    """
    Returns:
        (error_count, warning_count, error_messages, warning_messages)
    """
    error_count = 0
    warning_count = 0
    error_msgs: Counter[str] = Counter()
    warning_msgs: Counter[str] = Counter()

    line_lower = line.lower()

    error_match = ERROR_RE.search(line)
    if error_match or "error" in line_lower:
        error_count = 1
        msg = error_match.group(1) if error_match and error_match.group(1) else line
        error_msgs[normalize_message(msg)] += 1

    warning_match = WARNING_RE.search(line)
    if warning_match or "warning" in line_lower:
        warning_count = 1
        msg = warning_match.group(1) if warning_match and warning_match.group(1) else line
        warning_msgs[normalize_message(msg)] += 1

    return error_count, warning_count, error_msgs, warning_msgs


def analyze_chunk(lines: List[str]) -> AnalysisResult:
    result = AnalysisResult(total_lines=len(lines))
    for line in lines:
        ec, wc, em, wm = analyze_line(line)
        result.error_count += ec
        result.warning_count += wc
        result.error_messages.update(em)
        result.warning_messages.update(wm)
    return result


def chunkify(items: List[str], chunk_size: int) -> Iterable[List[str]]:
    for i in range(0, len(items), chunk_size):
        yield items[i:i + chunk_size]


def analyze_sequential(lines: List[str], show_progress: bool = True) -> AnalysisResult:
    result = AnalysisResult(total_lines=len(lines))
    total = len(lines)
    step = max(1, total // 10)

    for i, line in enumerate(lines, start=1):
        ec, wc, em, wm = analyze_line(line)
        result.error_count += ec
        result.warning_count += wc
        result.error_messages.update(em)
        result.warning_messages.update(wm)

        if show_progress and (i % step == 0 or i == total):
            pct = (i / total) * 100 if total else 100
            print(f"Sequential progress: {i}/{total} ({pct:.0f}%)")

    return result


def analyze_parallel(lines: List[str], workers: int = 4, show_progress: bool = True) -> AnalysisResult:
    if not lines:
        return AnalysisResult()

    # A reasonable chunk size for log files
    chunk_size = max(1, len(lines) // (workers * 4))
    chunks = list(chunkify(lines, chunk_size))

    result = AnalysisResult(total_lines=len(lines))
    completed = 0
    total_chunks = len(chunks)

    with ProcessPoolExecutor(max_workers=workers) as executor:
        for chunk_result in executor.map(analyze_chunk, chunks):
            result.merge(chunk_result)
            completed += 1
            if show_progress:
                pct = (completed / total_chunks) * 100
                print(f"Parallel progress: {completed}/{total_chunks} chunks ({pct:.0f}%)")

    return result


def print_report(title: str, result: AnalysisResult, elapsed: float) -> None:
    print("\n" + "=" * 60)
    print(title)
    print("=" * 60)
    print(f"Total lines scanned : {result.total_lines}")
    print(f"Error count         : {result.error_count}")
    print(f"Warning count       : {result.warning_count}")
    print(f"Execution time      : {elapsed:.6f} seconds")

    print("\nTop error messages:")
    for msg, count in result.error_messages.most_common(5):
        print(f"  {count} x {msg}")

    print("\nTop warning messages:")
    for msg, count in result.warning_messages.most_common(5):
        print(f"  {count} x {msg}")


def run_both(lines: List[str], workers: int) -> None:
    start = time.perf_counter()
    seq_result = analyze_sequential(lines, show_progress=True)
    seq_time = time.perf_counter() - start
    print_report("SEQUENTIAL ANALYSIS REPORT", seq_result, seq_time)

    start = time.perf_counter()
    par_result = analyze_parallel(lines, workers=workers, show_progress=True)
    par_time = time.perf_counter() - start
    print_report("PARALLEL ANALYSIS REPORT", par_result, par_time)

    speedup = seq_time / par_time if par_time > 0 else 0.0
    same_output = (
        seq_result.total_lines == par_result.total_lines
        and seq_result.error_count == par_result.error_count
        and seq_result.warning_count == par_result.warning_count
        and seq_result.error_messages == par_result.error_messages
        and seq_result.warning_messages == par_result.warning_messages
    )

    print("\n" + "=" * 60)
    print("COMPARISON SUMMARY")
    print("=" * 60)
    print(f"Sequential time : {seq_time:.6f} seconds")
    print(f"Parallel time   : {par_time:.6f} seconds")
    print(f"Speedup         : {speedup:.2f}x")
    print(f"Same result     : {'YES' if same_output else 'NO'}")


def main() -> None:
    parser = argparse.ArgumentParser(description="Parallel Log Error Analyzer")
    parser.add_argument("file", help="Path to the log file")
    parser.add_argument(
        "--mode",
        choices=["sequential", "parallel", "both"],
        default="both",
        help="Choose analysis mode",
    )
    parser.add_argument(
        "--workers",
        type=int,
        default=4,
        help="Number of worker processes for parallel mode",
    )
    args = parser.parse_args()

    path = Path(args.file)
    if not path.exists():
        raise FileNotFoundError(f"File not found: {path}")

    lines = path.read_text(encoding="utf-8", errors="ignore").splitlines()

    if args.mode == "sequential":
        start = time.perf_counter()
        result = analyze_sequential(lines, show_progress=True)
        elapsed = time.perf_counter() - start
        print_report("SEQUENTIAL ANALYSIS REPORT", result, elapsed)

    elif args.mode == "parallel":
        start = time.perf_counter()
        result = analyze_parallel(lines, workers=args.workers, show_progress=True)
        elapsed = time.perf_counter() - start
        print_report("PARALLEL ANALYSIS REPORT", result, elapsed)

    else:
        run_both(lines, workers=args.workers)


if __name__ == "__main__":
    main()