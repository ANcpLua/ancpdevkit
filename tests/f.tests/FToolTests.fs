namespace Tests

open System
open System.IO
open System.Text
open System.Diagnostics
open NUnit.Framework

[<TestFixture; Category("FTool")>]
type FToolIntegrationTests() =
    let rec findUp (startDir: string) (marker: string) =
        let full = Path.GetFullPath(startDir)
        let candidate = Path.Combine(full, marker)

        if File.Exists(candidate) then
            full
        else
            let parent = Directory.GetParent(full)

            if isNull parent then
                failwithf $"Could not locate '%s{marker}' above '%s{startDir}'"
            else
                findUp parent.FullName marker

    let repoRoot = lazy (findUp AppContext.BaseDirectory "ancpdevkit.sln")

    let run (args: string) =
        let psi = ProcessStartInfo("dotnet", args)
        psi.WorkingDirectory <- repoRoot.Value
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.StandardOutputEncoding <- Encoding.UTF8
        psi.StandardErrorEncoding <- Encoding.UTF8
        use p = Process.Start(psi)
        let outStr = p.StandardOutput.ReadToEnd()
        let errStr = p.StandardError.ReadToEnd()
        p.WaitForExit()
        p.ExitCode, outStr, errStr

    let runProj (projPath: string) (fileArg: string) (extra: string) =
        let bpsi = ProcessStartInfo("dotnet", $"build \"{projPath}\" -v minimal")
        bpsi.WorkingDirectory <- repoRoot.Value
        bpsi.RedirectStandardOutput <- true
        bpsi.RedirectStandardError <- true
        use bp = Process.Start(bpsi)
        bp.WaitForExit()
        let extraPart = if String.IsNullOrWhiteSpace(extra) then "" else extra + " "
        run $"run --no-build --project {projPath} --framework net9.0 -- {extraPart}{fileArg}"

    [<Test>]
    member _.``f counts and strips text correctly``() =
        let tmp =
            Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "f_it_" + Guid.NewGuid().ToString("N")))

        try
            let sample = Path.Combine(tmp.FullName, "sample.txt")

            File.WriteAllText(
                sample,
                "# heading\nHello, world! Don't split.<!-- hide --> Visible\n",
                UTF8Encoding(false)
            )

            let proj = Path.Combine(repoRoot.Value, "tools", "ancp", "f", "f.fsproj")
            let ec1, out1, err1 = runProj proj sample ""
            Assert.That(ec1, Is.EqualTo(0), err1)
            Assert.That(String.IsNullOrWhiteSpace(err1), Is.True)
            Assert.That(out1.Trim().Length, Is.GreaterThan(0))

            let ec2, out2, _ = runProj proj sample "--unique"
            Assert.That(ec2, Is.EqualTo(0))
            Assert.That(out2.Trim().Length, Is.GreaterThan(0))

            let ec3, out3, _ = runProj proj sample "--strip "
            Assert.That(ec3, Is.EqualTo(0))
            Assert.That(out3.Contains("# heading"), Is.False)
            Assert.That(out3.Contains("<!-- hide -->"), Is.False)
            Assert.That(out3.Contains("Don't split."), Is.True)
        finally
            try
                Directory.Delete(tmp.FullName, true)
            with _ ->
                ()

    [<Test>]
    member _.``f reads docx``() =
        let tmp =
            Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "f_docx_" + Guid.NewGuid().ToString("N")))

        try
            let docXml =
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:body>
    <w:p><w:r><w:t>Hello Word DOCX</w:t></w:r></w:p>
    <w:p><w:r><w:t>Count me 2 times</w:t></w:r></w:p>
  </w:body>
</w:document>           
"""

            let docxPath = Path.Combine(tmp.FullName, "sample.docx")

            do
                use fs = File.Create(docxPath)

                use za =
                    new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create)

                let entry = za.CreateEntry("word/document.xml")
                use es = entry.Open()
                let bytes = Encoding.UTF8.GetBytes(docXml)
                es.Write(bytes, 0, bytes.Length)

            let proj = Path.Combine(repoRoot.Value, "tools", "ancp", "f", "f.fsproj")
            let ec, outStr, err = runProj proj docxPath ""
            Assert.That(ec, Is.EqualTo(0), err)
            Assert.That(outStr.Trim().Length, Is.GreaterThan(0))
        finally
            try
                Directory.Delete(tmp.FullName, true)
            with _ ->
                ()

    [<Test>]
    member _.``f unicode + digits: counts and uniques``() =
        let tmp =
            Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "f_unicode_" + Guid.NewGuid().ToString("N")))

        try
            let sample = Path.Combine(tmp.FullName, "sample.txt")

            let content =
                """
Café CAFÉ café
Don't don’t don't
HTTP2 http2 2025 2025
"""

            File.WriteAllText(sample, content, UTF8Encoding(false))
            let proj = Path.Combine(repoRoot.Value, "tools", "ancp", "f", "f.fsproj")
            let ecTotal, outTotal, err1 = runProj proj sample ""
            Assert.That(ecTotal, Is.EqualTo(0), err1)
            Assert.That(outTotal.Trim(), Is.EqualTo("10"))

            let ecUnique, outUnique, err2 = runProj proj sample "--unique"
            Assert.That(ecUnique, Is.EqualTo(0), err2)
            Assert.That(outUnique.Trim(), Is.EqualTo("5"))
        finally
            try
                Directory.Delete(tmp.FullName, true)
            with _ ->
                ()

[<TestFixture; Category("FTool.Unit")>]
type FToolUnitTests() =
    [<Test>]
    member _.``tryReadPdf INLINE and FAIL``() =
        let tmp = Path.Combine(Path.GetTempPath(), "f_pdf_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tmp) |> ignore

        try
            let pdfPath = Path.Combine(tmp, "sample.pdf")
            let content = "Hello from inline PDF"
            File.WriteAllText(pdfPath, content, UTF8Encoding(false))

            Environment.SetEnvironmentVariable("ANCP_PDFTOTEXT", "INLINE")
            let ok = Program.tryReadPdf pdfPath
            Assert.That(ok, Is.EqualTo(Some content))

            Environment.SetEnvironmentVariable("ANCP_PDFTOTEXT", "FAIL")
            let bad = Program.tryReadPdf pdfPath
            Assert.That(bad, Is.EqualTo(None))
        finally
            Environment.SetEnvironmentVariable("ANCP_PDFTOTEXT", null)

            try
                Directory.Delete(tmp, true)
            with _ ->
                ()

    [<Test>]
    member _.``tryReadPdf default (pdftotext missing) and custom exe success``() =
        let tmp = Path.Combine(Path.GetTempPath(), "f_pdf_custom_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tmp) |> ignore

        try
            let pdfPath = Path.Combine(tmp, "doc.pdf")
            let content = "Hello from custom exe"
            File.WriteAllText(pdfPath, content, UTF8Encoding(false))

            Environment.SetEnvironmentVariable("ANCP_PDFTOTEXT", null)
            let noneDefault = Program.tryReadPdf pdfPath
            Assert.That(noneDefault, Is.EqualTo(None))

            let toolPath = Path.Combine(tmp, "echo_pdf.sh")
            let script =
                """#!/usr/bin/env bash
set -euo pipefail
# Find first arg that is an existing file and cat it
for a in "$@"; do
  if [ -f "$a" ]; then
    cat "$a"
    exit 0
  fi
done
exit 1
"""
            File.WriteAllText(toolPath, script, UTF8Encoding(false))

            if not (OperatingSystem.IsWindows()) then
                try
                    File.SetUnixFileMode(toolPath, UnixFileMode.UserRead |||
                                                           UnixFileMode.UserExecute |||
                                                           UnixFileMode.UserWrite |||
                                                           UnixFileMode.GroupRead |||
                                                           UnixFileMode.GroupExecute |||
                                                           UnixFileMode.OtherRead |||
                                                           UnixFileMode.OtherExecute)
                with _ ->
                    let psi = ProcessStartInfo("/bin/chmod", $"+x \"{toolPath}\"")
                    psi.RedirectStandardError <- true
                    psi.RedirectStandardOutput <- true
                    use p = Process.Start(psi)
                    p.WaitForExit()

            Environment.SetEnvironmentVariable("ANCP_PDFTOTEXT", toolPath)
            let ok = Program.tryReadPdf pdfPath
            Assert.That(ok, Is.EqualTo(Some content))
        finally
            Environment.SetEnvironmentVariable("ANCP_PDFTOTEXT", null)
            try Directory.Delete(tmp, true) with _ -> ()

    [<Test>]
    member _.``tryReadPdf default success via PATH stub (non-Windows)``() =
        if OperatingSystem.IsWindows() then
            Assert.Pass("Skipped on Windows")
        else
            let tmp = Path.Combine(Path.GetTempPath(), "f_pdf_path_" + Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(tmp) |> ignore
            let oldPath = Environment.GetEnvironmentVariable("PATH")
            try
                let pdfPath = Path.Combine(tmp, "doc.pdf")
                let content = "Hello from PATH pdftotext"
                File.WriteAllText(pdfPath, content, UTF8Encoding(false))

                let toolDir = Directory.CreateDirectory(Path.Combine(tmp, "bin")).FullName
                let toolPath = Path.Combine(toolDir, "pdftotext")
                let script =
                    """#!/usr/bin/env bash
set -euo pipefail
for a in "$@"; do
  if [ -f "$a" ]; then
    cat "$a"
    exit 0
  fi
done
exit 1
"""
                File.WriteAllText(toolPath, script, UTF8Encoding(false))
                File.SetUnixFileMode(toolPath,
                    UnixFileMode.UserRead ||| UnixFileMode.UserExecute |||
                    UnixFileMode.UserWrite ||| UnixFileMode.GroupRead |||
                    UnixFileMode.GroupExecute ||| UnixFileMode.OtherRead |||
                    UnixFileMode.OtherExecute)

                let newPath = (toolDir + ":" + (if String.IsNullOrEmpty(oldPath) then "" else oldPath))
                Environment.SetEnvironmentVariable("PATH", newPath)
                Environment.SetEnvironmentVariable("ANCP_PDFTOTEXT", null)

                let res = Program.tryReadPdf pdfPath
                Assert.That(res, Is.EqualTo(Some content))
            finally
                Environment.SetEnvironmentVariable("PATH", oldPath)
                Environment.SetEnvironmentVariable("ANCP_PDFTOTEXT", null)
                try Directory.Delete(tmp, true) with _ -> ()

    [<Test>]
    member _.``tryReadPdf custom exe failure returns None``() =
        let tmp = Path.Combine(Path.GetTempPath(), "f_pdf_custom_fail_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tmp) |> ignore
        try
            let pdfPath = Path.Combine(tmp, "doc.pdf")
            File.WriteAllText(pdfPath, "content", UTF8Encoding(false))

            let toolPath = Path.Combine(tmp, "fail_pdf.sh")
            let script =
                """#!/usr/bin/env bash
exit 3
"""
            File.WriteAllText(toolPath, script, UTF8Encoding(false))
            if not (OperatingSystem.IsWindows()) then
                try
                    File.SetUnixFileMode(toolPath, UnixFileMode.UserRead |||
                                                           UnixFileMode.UserExecute)
                with _ ->
                    let psi = ProcessStartInfo("/bin/chmod", $"+x \"{toolPath}\"")
                    psi.RedirectStandardError <- true
                    psi.RedirectStandardOutput <- true
                    use p = Process.Start(psi)
                    p.WaitForExit()

            Environment.SetEnvironmentVariable("ANCP_PDFTOTEXT", toolPath)
            let res = Program.tryReadPdf pdfPath
            Assert.That(res, Is.EqualTo(None))
        finally
            Environment.SetEnvironmentVariable("ANCP_PDFTOTEXT", null)
            try Directory.Delete(tmp, true) with _ -> ()

    [<Test>]
    member _.``main handles UnauthorizedAccessException``() =
        if OperatingSystem.IsWindows() then
            Assert.Pass("Skipped on Windows")
        else
            let tmp = Path.Combine(Path.GetTempPath(), "f_unauth_" + Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(tmp) |> ignore
            try
                let path = Path.Combine(tmp, "secret.txt")
                File.WriteAllText(path, "secret", UTF8Encoding(false))

                File.SetUnixFileMode(path, enum 0)

                let repoRoot =
                    let rec findUp (startDir: string) (marker: string) =
                        let full = Path.GetFullPath(startDir)
                        let candidate = Path.Combine(full, marker)
                        if File.Exists(candidate) then full
                        else
                            let parent = Directory.GetParent(full)
                            if isNull parent then failwithf $"Could not locate '%s{marker}' above '%s{startDir}'"
                            else findUp parent.FullName marker
                    findUp AppContext.BaseDirectory "ancpdevkit.sln"

                let proj = Path.Combine(repoRoot, "tools", "ancp", "f", "f.fsproj")
                let psi = ProcessStartInfo("dotnet", $"run --no-build --project {proj} --framework net9.0 -- {path}")
                psi.WorkingDirectory <- repoRoot
                psi.RedirectStandardOutput <- true
                psi.RedirectStandardError <- true
                use p = Process.Start(psi)
                let _outStr = p.StandardOutput.ReadToEnd()
                let errStr = p.StandardError.ReadToEnd()
                p.WaitForExit()

                Assert.That(p.ExitCode, Is.EqualTo(1))
                Assert.That(errStr.Contains("Error:"), Is.True)
            finally
                try File.SetUnixFileMode(Path.Combine(tmp, "secret.txt"),
                        UnixFileMode.UserRead ||| UnixFileMode.UserWrite) with _ -> ()
                try Directory.Delete(tmp, true) with _ -> ()

    [<Test>]
    member _.``readInput pdf FAIL propagates to error``() =
        let tmp = Path.Combine(Path.GetTempPath(), "f_pdf_fail_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tmp) |> ignore

        try
            let pdfPath = Path.Combine(tmp, "doc.pdf")
            File.WriteAllText(pdfPath, "dummy", UTF8Encoding(false))
            Environment.SetEnvironmentVariable("ANCP_PDFTOTEXT", "FAIL")
            Assert.Throws<Exception>(fun () -> Program.readInput [| pdfPath |] |> ignore) |> ignore
        finally
            Environment.SetEnvironmentVariable("ANCP_PDFTOTEXT", null)
            try Directory.Delete(tmp, true) with _ -> ()

    [<Test>]
    member _.``readInput handles pdf and invalid args``() =
        let tmp = Path.Combine(Path.GetTempPath(), "f_inp_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tmp) |> ignore

        try
            let pdfPath = Path.Combine(tmp, "doc.pdf")
            let content = "PDF body text"
            File.WriteAllText(pdfPath, content, UTF8Encoding(false))
            Environment.SetEnvironmentVariable("ANCP_PDFTOTEXT", "INLINE")
            let txt = Program.readInput [| pdfPath |]
            Assert.That(txt, Is.EqualTo(content))

            Assert.Throws<Exception>(fun () -> Program.readInput [||] |> ignore) |> ignore

            Assert.Throws<Exception>(fun () -> Program.readInput [| "a"; "b" |] |> ignore)
            |> ignore
        finally
            Environment.SetEnvironmentVariable("ANCP_PDFTOTEXT", null)

            try
                Directory.Delete(tmp, true)
            with _ ->
                ()

    [<Test>]
    member _.``readDocx returns empty when document.xml missing``() =
        let tmp =
            Path.Combine(Path.GetTempPath(), "f_docx_empty_" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory(tmp) |> ignore

        try
            let docxPath = Path.Combine(tmp, "empty.docx")

            do
                use fs = File.Create(docxPath)

                use za =
                    new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create)

                let _ = za.CreateEntry("word/styles.xml")
                ()

            let text = Program.readDocx docxPath
            Assert.That(text, Is.EqualTo(""))
        finally
            try
                Directory.Delete(tmp, true)
            with _ ->
                ()

    [<Test>]
    member _.``main prints help and handles missing file``() =
        let repoRoot =
            let rec findUp (startDir: string) (marker: string) =
                let full = Path.GetFullPath(startDir)
                let candidate = Path.Combine(full, marker)

                if File.Exists(candidate) then
                    full
                else
                    let parent = Directory.GetParent(full)

                    if isNull parent then
                        failwithf $"Could not locate '%s{marker}' above '%s{startDir}'"
                    else
                        findUp parent.FullName marker

            findUp AppContext.BaseDirectory "ancpdevkit.sln"

        let proj = Path.Combine(repoRoot, "tools", "ancp", "f", "f.fsproj")

        let run (args: string) =
            let psi = ProcessStartInfo("dotnet", args)
            psi.WorkingDirectory <- repoRoot
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            psi.StandardOutputEncoding <- Encoding.UTF8
            psi.StandardErrorEncoding <- Encoding.UTF8
            use p: Process = Process.Start(psi)
            let outStr = p.StandardOutput.ReadToEnd()
            let errStr = p.StandardError.ReadToEnd()
            p.WaitForExit()
            p.ExitCode, outStr, errStr

        let ecHelp, outHelp, errHelp =
            run $"run --no-build --project {proj} --framework net9.0 -- --help"

        Assert.That(ecHelp, Is.EqualTo(0), errHelp)
        Assert.That(outHelp.Contains("Usage:"), Is.True)

        let ecMissing, _, errMissing =
            run $"run --no-build --project {proj} --framework net9.0 -- missing_does_not_exist.txt"

        Assert.That(ecMissing, Is.EqualTo(1))
        Assert.That(errMissing.Contains("Error:"), Is.True)

    [<Test>]
    member _.``--chars option counts characters``() =
        let tmp = Path.Combine(Path.GetTempPath(), "f_chars_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tmp) |> ignore

        try
            let testFile = Path.Combine(tmp, "test.txt")
            let content = "Hello World!\nTest 123"
            File.WriteAllText(testFile, content)
            
            let repoRoot =
                let rec findUp (startDir: string) (marker: string) =
                    let full = Path.GetFullPath(startDir)
                    let candidate = Path.Combine(full, marker)
                    if File.Exists(candidate) then
                        full
                    else
                        let parent = Directory.GetParent(full)
                        if isNull parent then
                            failwithf $"Could not locate '%s{marker}' above '%s{startDir}'"
                        else
                            findUp parent.FullName marker
                findUp AppContext.BaseDirectory "ancpdevkit.sln"
            
            let proj = Path.Combine(repoRoot, "tools", "ancp", "f", "f.fsproj")
            let psi = ProcessStartInfo("dotnet", $"run --no-build --project {proj} --framework net9.0 -- --chars {testFile}")
            psi.WorkingDirectory <- repoRoot
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            use p = Process.Start(psi)
            let output = p.StandardOutput.ReadToEnd()
            p.WaitForExit()
            
            Assert.That(p.ExitCode, Is.EqualTo(0))
            Assert.That(output.Trim(), Is.EqualTo(content.Length.ToString()))
        finally
            try Directory.Delete(tmp, true) with _ -> ()

    [<Test>]
    member _.``--lines option counts lines``() =
        let tmp = Path.Combine(Path.GetTempPath(), "f_lines_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tmp) |> ignore

        try
            let testFile = Path.Combine(tmp, "test.txt")
            let content = "Line 1\nLine 2\nLine 3"
            File.WriteAllText(testFile, content)
            
            let repoRoot =
                let rec findUp (startDir: string) (marker: string) =
                    let full = Path.GetFullPath(startDir)
                    let candidate = Path.Combine(full, marker)
                    if File.Exists(candidate) then
                        full
                    else
                        let parent = Directory.GetParent(full)
                        if isNull parent then
                            failwithf $"Could not locate '%s{marker}' above '%s{startDir}'"
                        else
                            findUp parent.FullName marker
                findUp AppContext.BaseDirectory "ancpdevkit.sln"
            
            let proj = Path.Combine(repoRoot, "tools", "ancp", "f", "f.fsproj")
            let psi = ProcessStartInfo("dotnet", $"run --no-build --project {proj} --framework net9.0 -- --lines {testFile}")
            psi.WorkingDirectory <- repoRoot
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            use p = Process.Start(psi)
            let output = p.StandardOutput.ReadToEnd()
            p.WaitForExit()
            
            Assert.That(p.ExitCode, Is.EqualTo(0))
            Assert.That(output.Trim(), Is.EqualTo("3"))
        finally
            try Directory.Delete(tmp, true) with _ -> ()

    [<Test>]
    member _.``--no-numbers option excludes numeric words``() =
        let tmp = Path.Combine(Path.GetTempPath(), "f_no_nums_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tmp) |> ignore

        try
            let testFile = Path.Combine(tmp, "test.txt")
            let content = "Hello 123 World 456 Test"
            File.WriteAllText(testFile, content)
            
            let repoRoot =
                let rec findUp (startDir: string) (marker: string) =
                    let full = Path.GetFullPath(startDir)
                    let candidate = Path.Combine(full, marker)
                    if File.Exists(candidate) then
                        full
                    else
                        let parent = Directory.GetParent(full)
                        if isNull parent then
                            failwithf $"Could not locate '%s{marker}' above '%s{startDir}'"
                        else
                            findUp parent.FullName marker
                findUp AppContext.BaseDirectory "ancpdevkit.sln"
            
            let proj = Path.Combine(repoRoot, "tools", "ancp", "f", "f.fsproj")
            
            let psi1 = ProcessStartInfo("dotnet", $"run --no-build --project {proj} --framework net9.0 -- {testFile}")
            psi1.WorkingDirectory <- repoRoot
            psi1.RedirectStandardOutput <- true
            psi1.RedirectStandardError <- true
            use p1 = Process.Start(psi1)
            let output1 = p1.StandardOutput.ReadToEnd()
            p1.WaitForExit()
            
            let psi2 = ProcessStartInfo("dotnet", $"run --no-build --project {proj} --framework net9.0 -- --no-numbers {testFile}")
            psi2.WorkingDirectory <- repoRoot
            psi2.RedirectStandardOutput <- true
            psi2.RedirectStandardError <- true
            use p2 = Process.Start(psi2)
            let output2 = p2.StandardOutput.ReadToEnd()
            p2.WaitForExit()
            
            Assert.That(p1.ExitCode, Is.EqualTo(0))
            Assert.That(p2.ExitCode, Is.EqualTo(0))
            Assert.That(output1.Trim(), Is.EqualTo("5"))
            Assert.That(output2.Trim(), Is.EqualTo("3"))
        finally
            try Directory.Delete(tmp, true) with _ -> ()

    [<Test>]
    member _.``main handles malformed docx (generic catch)``() =
        let tmp = Path.Combine(Path.GetTempPath(), "f_docx_bad_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tmp) |> ignore
        try
            let docxPath = Path.Combine(tmp, "bad.docx")
            do
                use fs = File.Create(docxPath)
                use za = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create)
                let entry = za.CreateEntry("word/document.xml")
                use es = entry.Open()
                let bytes = Encoding.UTF8.GetBytes("<w:document>oops")
                es.Write(bytes, 0, bytes.Length)

            let repoRoot =
                let rec findUp (startDir: string) (marker: string) =
                    let full = Path.GetFullPath(startDir)
                    let candidate = Path.Combine(full, marker)
                    if File.Exists(candidate) then full else
                    let parent = Directory.GetParent(full)
                    if isNull parent then failwithf $"Could not locate '%s{marker}' above '%s{startDir}'" else
                    findUp parent.FullName marker
                findUp AppContext.BaseDirectory "ancpdevkit.sln"

            let proj = Path.Combine(repoRoot, "tools", "ancp", "f", "f.fsproj")
            let psi = ProcessStartInfo("dotnet", $"run --no-build --project {proj} --framework net9.0 -- {docxPath}")
            psi.WorkingDirectory <- repoRoot
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            use p = Process.Start(psi)
            let _outStr = p.StandardOutput.ReadToEnd()
            let errStr = p.StandardError.ReadToEnd()
            p.WaitForExit()

            Assert.That(p.ExitCode, Is.EqualTo(1))
            Assert.That(errStr.Contains("Error:"), Is.True)
        finally
            try Directory.Delete(tmp, true) with _ -> ()
