.PHONY: build install test clean

build:
	dotnet build tools/ancp/f/f.fsproj -c Release -v minimal
	dotnet build tools/ancp/fc/fc.fsproj -c Release -v minimal

install: build
	(dotnet tool install --global --add-source ./tools/ancp/f/nupkg ancp \
	 || dotnet tool update --global --add-source ./tools/ancp/f/nupkg ancp) || true
	(dotnet tool install --global --add-source ./tools/ancp/fc/nupkg --prerelease ancp.fc \
	 || dotnet tool update --global --add-source ./tools/ancp/fc/nupkg --prerelease ancp.fc) || true

test:
	dotnet test tests/f.tests/f.tests.fsproj -v minimal
	dotnet test tests/fc.tests/fc.tests.fsproj -v minimal

clean:
	rm -rf tools/ancp/f/bin tools/ancp/f/obj tools/ancp/f/nupkg \
	       tools/ancp/fc/bin tools/ancp/fc/obj tools/ancp/fc/nupkg \
	       tests/f.tests/bin tests/f.tests/obj \
	       tests/fc.tests/bin tests/fc.tests/obj || true
