#!/bin/env bash
set -eu

pushd FenLoader && dotnet build -c Release
popd

if [ ! -d launcher/build ]; then
	mkdir launcher/build
	pushd launcher/build
	meson --cross-file ../cross_file.txt ..
	popd
fi

pushd launcher/build && ninja
