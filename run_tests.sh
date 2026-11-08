#!/bin/bash
# Runs quiet mode on the test folders and compares every log with its expected file.
#   usage: ./run_tests.sh [folder...]     (default: error_tests regression_tests)
#   ROBOT can point at another build of the program.
set -u
ROBOT="${ROBOT:-./publish/robot}"
folders=("$@")
[ ${#folders[@]} -eq 0 ] && folders=(error_tests regression_tests)
out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT

pass=0
fail=0
for dir in "${folders[@]}"; do
    for f in "$dir"/input_*.txt; do
        id="$(basename "$f" .txt)"
        id="${id#input_}"
        "$ROBOT" -q "$f" "$out/$id.txt" > /dev/null 2>&1
        if diff -q --strip-trailing-cr "$dir/expected_$id.txt" "$out/$id.txt" > /dev/null 2>&1; then
            pass=$((pass + 1))
        else
            fail=$((fail + 1))
            echo "FAIL $dir/$id"
            diff --strip-trailing-cr "$dir/expected_$id.txt" "$out/$id.txt" | head -6
        fi
    done
done
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
