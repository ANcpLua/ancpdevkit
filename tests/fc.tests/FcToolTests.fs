namespace Tests

open System
open System.IO
open System.Text
open System.Diagnostics
open NUnit.Framework
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp

[<TestFixture; Category("FCTool")>]
type FcToolTests() =
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

    let runProj (projPath: string) (pathArg: string) =
        let bpsi = ProcessStartInfo("dotnet", $"build \"{projPath}\" -v minimal")
        bpsi.WorkingDirectory <- repoRoot.Value
        bpsi.RedirectStandardOutput <- true
        bpsi.RedirectStandardError <- true
        use bp = Process.Start(bpsi)
        bp.WaitForExit()
        run $"run --no-build --project {projPath} --framework net9.0 -- {pathArg}"

    [<Test>]
    member _.``fc removes comments in-place, preserves strings, idempotent``() =
        let tmp =
            Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "fc_it_" + Guid.NewGuid().ToString("N")))

        try
            let sample = Path.Combine(tmp.FullName, "sample.cs")

            let input =
                """
using System; // import
class C /* type */
{
  string s = "/* not comment */ // neither"; // trailing
  /// <summary>Doc</summary>
  void M() {
    #region region
    System.Console.WriteLine(1 + 2); /* math */
    #endregion
  }
}
"""

            File.WriteAllText(sample, input, UTF8Encoding(false))

            let proj = Path.Combine(repoRoot.Value, "tools", "ancp", "fc", "fc.fsproj")
            let ec1, _, err1 = runProj proj sample
            Assert.That(ec1, Is.EqualTo(0), err1)
            let after1 = File.ReadAllText(sample, Encoding.UTF8)
            Assert.That(after1.Contains("// import"), Is.False)
            Assert.That(after1.Contains("/* type */"), Is.False)
            Assert.That(after1.Contains("/* math */"), Is.False)
            Assert.That(after1.Contains("#region"), Is.False)
            Assert.That(after1.Contains("#endregion"), Is.False)
            Assert.That(after1.Contains("/* not comment */ // neither"), Is.True)

            let sample2 = Path.Combine(tmp.FullName, "sample2.cs")
            File.WriteAllText(sample2, after1, UTF8Encoding(false))
            let ec2, _, err2 = runProj proj sample2
            Assert.That(ec2, Is.EqualTo(0), err2)
            let after2 = File.ReadAllText(sample2, Encoding.UTF8)
            Assert.That(after2, Is.EqualTo(after1))
        finally
            try
                Directory.Delete(tmp.FullName, true)
            with _ ->
                ()

    [<Test>]
    member _.``fc semantic tokens unchanged (ignoring trivia)``() =
        let input =
            """
using System; // import
class C /* type */
{
  string s = "/* not comment */ // neither"; // trailing
  /// <summary>Doc</summary>
  void M() {
    #region region
    System.Console.WriteLine(1 + 2); /* math */
    #endregion
  }
}
"""

        let cleaned = FcTool.CommentStripper.strip input

        let opts =
            CSharpParseOptions(languageVersion = LanguageVersion.Latest, documentationMode = DocumentationMode.Parse)

        let toks (src: string) =
            CSharpSyntaxTree.ParseText(src, opts).GetRoot().DescendantTokens()
            |> Seq.map (fun t -> t.Kind(), t.Text)
            |> Seq.toArray

        let a = toks input
        let b = toks cleaned
        Assert.That(b.Length, Is.EqualTo(a.Length))

        for i in 0 .. a.Length - 1 do
            Assert.That(fst b[i], Is.EqualTo(fst a[i]))
            Assert.That(snd b[i], Is.EqualTo(snd a[i]))

    [<Test>]
    member _.``fc directory recursion processes all cs files``() =
        let tmp =
            Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "fc_dir_" + Guid.NewGuid().ToString("N")))

        try
            let dir1 = tmp.FullName
            let dir2 = Directory.CreateDirectory(Path.Combine(tmp.FullName, "nested")).FullName
            let cs1 = Path.Combine(dir1, "a.cs")
            let cs2 = Path.Combine(dir2, "b.cs")
            File.WriteAllText(cs1, "class A { // c\nstring s=\"x/*y*/\"; /* z */}\n", UTF8Encoding(false))
            File.WriteAllText(cs2, "#region r\nclass B{ /* m */ }\n#endregion\n", UTF8Encoding(false))

            let proj = Path.Combine(repoRoot.Value, "tools", "ancp", "fc", "fc.fsproj")
            let ec, _, err = runProj proj dir1
            Assert.That(ec, Is.EqualTo(0), err)
            let t1 = File.ReadAllText(cs1, Encoding.UTF8)
            let t2 = File.ReadAllText(cs2, Encoding.UTF8)
            Assert.That(t1.Contains("// c") || t1.Contains("/* z */"), Is.False)
            Assert.That(t2.Contains("#region") || t2.Contains("#endregion") || t2.Contains("/* m */"), Is.False)
        finally
            try
                Directory.Delete(tmp.FullName, true)
            with _ ->
                ()

    [<Test>]
    member _.``fc non-cs file returns error code 2``() =
        let tmp = Path.Combine(Path.GetTempPath(), "fc_err_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tmp) |> ignore

        try
            let txt = Path.Combine(tmp, "note.txt")
            File.WriteAllText(txt, "not cs", UTF8Encoding(false))
            let proj = Path.Combine(repoRoot.Value, "tools", "ancp", "fc", "fc.fsproj")
            let ec, _, _ = runProj proj txt
            Assert.That(ec, Is.EqualTo(2))
        finally
            try
                Directory.Delete(tmp, true)
            with _ ->
                ()

    [<Test>]
    member _.``fc path not found returns 1``() =
        let proj = Path.Combine(repoRoot.Value, "tools", "ancp", "fc", "fc.fsproj")
        let ec, _, err = runProj proj "__definitely_missing__"
        Assert.That(ec, Is.EqualTo(1), err)

[<TestFixture; Category("FCTool.Unit")>]
type FcToolUnitTests() =
    [<Test>]
    member _.``Cli.parse and toSettings parse all flags``() =
        let argv =
            [| "-h"
               "-V"
               "-i"
               "-r"
               "--backup"
               ".bak"
               "-o"
               "out.txt"
               "--keep-docs"
               "--keep-headers"
               "--keep-regions"
               "--keep-if-match"
               "TODO"
               "--keep-if-match"
               "FIXME"
               "file1.cs"
               "dir" |]

        let o = FcTool.Cli.parse argv
        Assert.That(o.ShowHelp, Is.True)
        Assert.That(o.ShowVersion, Is.True)
        Assert.That(o.InPlace, Is.True)
        Assert.That(o.Recurse, Is.True)
        Assert.That(o.BackupSuffix, Is.EqualTo(Some ".bak"))
        Assert.That(o.OutputPath, Is.EqualTo(Some "out.txt"))
        Assert.That(o.Inputs, Is.EquivalentTo([ "file1.cs"; "dir" ]))
        Assert.That(o.KeepDocs && o.KeepHeaders && o.KeepRegions, Is.True)
        Assert.That(o.KeepIfMatch, Is.EquivalentTo([ "TODO"; "FIXME" ]))
        let s = FcTool.Cli.toSettings o
        Assert.That(s.KeepDocs && s.KeepHeaders && s.KeepRegions, Is.True)
        Assert.That(s.KeepIfMatch, Is.EquivalentTo([ "TODO"; "FIXME" ]))

    [<Test>]
    member _.``CommentStripper flags preserve accordingly``() =
        let src =
            """
// header license
// next line
using System; // import
/// <summary>Doc</summary>
class C {
    string a = "/* not */"; /* remove */
    #region r
    int x; // trailing
    #endregion
}
// tail
"""

        let s1 =
            FcTool.CommentStripper.stripWith
                { KeepDocs = true
                  KeepHeaders = false
                  KeepRegions = false
                  KeepIfMatch = [] }
                src

        Assert.That(s1.Contains("/// <summary>Doc</summary>"), Is.True)
        Assert.That(s1.Contains("// header license"), Is.False)
        Assert.That(s1.Contains("#region"), Is.False)

        let s2 =
            FcTool.CommentStripper.stripWith
                { KeepDocs = false
                  KeepHeaders = true
                  KeepRegions = false
                  KeepIfMatch = [] }
                src

        Assert.That(s2.Contains("// header license"), Is.True)
        Assert.That(s2.Contains("/// <summary>Doc</summary>"), Is.False)

        let s3 =
            FcTool.CommentStripper.stripWith
                { KeepDocs = false
                  KeepHeaders = false
                  KeepRegions = true
                  KeepIfMatch = [] }
                src

        Assert.That(s3.Contains("#region"), Is.True)

        let s4 =
            FcTool.CommentStripper.stripWith
                { KeepDocs = false
                  KeepHeaders = false
                  KeepRegions = false
                  KeepIfMatch = [ "trailing" ] }
                src

        Assert.That(s4.Contains("// trailing"), Is.True)
        Assert.That(s4.Contains("/* remove */"), Is.False)
