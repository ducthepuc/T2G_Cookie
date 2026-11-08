#!/bin/bash
# Runs the program in quiet mode on every input_NNN.txt of a folder, writes the
# matching output_NNN.txt files and packs them into results.zip.
#   usage: ./run_all.sh <input folder> <output folder>
#   ROBOT can point at another build of the program.
set -u
ROBOT="${ROBOT:-./publish/robot}"
input="${1:?usage: run_all.sh <input folder> <output folder>}"
output="${2:?usage: run_all.sh <input folder> <output folder>}"
mkdir -p "$output"

failed=0
for f in "$input"/input_*.txt; do
    id="$(basename "$f" .txt)"
    id="${id#input_}"
    "$ROBOT" -q "$f" "$output/output_$id.txt" > /dev/null || { echo "FAILED: $f"; failed=$((failed + 1)); }
done

rm -f results.zip
(cd "$output" && zip -q ../results.zip output_*.txt) || echo "zip failed: pack $output by hand"
echo "done, $failed failed"
