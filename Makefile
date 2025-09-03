.PHONY: build install test clean

build:
	dotnet build tools/ancp/f/f.fsproj -c Release -v minimal
	dotnet build tools/ancp/fcc/fcc.fsproj -c Release -v minimal

install: build
	(dotnet tool install --global --add-source ./tools/ancp/f/nupkg ancp \
	 || dotnet tool update --global --add-source ./tools/ancp/f/nupkg ancp) || true
	(dotnet tool install --global --add-source ./tools/ancp/fcc/nupkg ancp.fcc \
	 || dotnet tool update --global --add-source ./tools/ancp/fcc/nupkg ancp.fcc) || true

test:
	dotnet test tests/f.tests/f.tests.fsproj -v minimal
	dotnet test tests/fcc.tests/fcc.tests.fsproj -v minimal

clean:
	rm -rf tools/ancp/f/bin tools/ancp/f/obj tools/ancp/f/nupkg \
	       tools/ancp/fcc/bin tools/ancp/fcc/obj tools/ancp/fcc/nupkg \
	       tests/f.tests/bin tests/f.tests/obj \
	       tests/fcc.tests/bin tests/fcc.tests/obj || true
