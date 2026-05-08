# CS325

# Parallel Log Error Analyzer

A theory-driven parallel programming project that compares sequential and parallel processing using the same real-world problem: analyzing large log files for errors and warnings.

## Overview

This project demonstrates how a large log file can be processed by:
- a sequential version,
- a parallel Python version, and
- a parallel C# version.

The goal is to compare correctness, execution time, and readability while showing how parallel programming can improve log analysis tasks used in real-world system monitoring and debugging.

## Real-World Use

Log analysis is common in:
- software debugging
- server monitoring
- system administration
- security review
- incident investigation

This project scans log entries and counts:
- errors
- warnings
- total lines processed
- repeated messages
- execution time

## Project Structure


Requirements:
Python
Python 3.x


C#
.NET SDK installed
Check with:
dotnet --version
Python Version

Run sequential and parallel analysis
python log_analyzer.py sample_logs/sample.log --mode both
Run only sequential
python log_analyzer.py sample_logs/sample.log --mode sequential
Run only parallel
python log_analyzer.py sample_logs/sample.log --mode parallel --workers 4

C# Version
Run both modes
dotnet run -- sample_logs/sample.log both
Run only sequential
dotnet run -- sample_logs/sample.log sequential
Run only parallel
dotnet run -- sample_logs/sample.log parallel


Expected Output

The program will display:

total lines scanned
number of errors
number of warnings
top repeated messages
execution time
speedup comparison between sequential and parallel versions
