#!/bin/sh
set -eu
# The desktop entry invokes /bin/sh with this path as an argument. Some launchers
# check executable existence before expanding %% in the first Exec token.
app_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
exec "$app_dir/SlickWatch" "$@"
