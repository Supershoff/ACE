#!/usr/bin/env bash

set -euo pipefail

result="$(cat)"

grep -Eqi \
  'still waiting|agents? (are|is) .*running|waiting (on|for).*(background|research|survey|implementation|test).*(agent|work|completion|finish)|background .*(agent|test|work).*(running|finish|complete)|try again|temporar|rate limit|server-side' \
  <<<"${result}"
