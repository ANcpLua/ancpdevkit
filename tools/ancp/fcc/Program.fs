namespace FccTool

open System
open System.IO
open System.Text
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp

module CommentStripper =
    type StripSettings =
        { KeepDocs: bool
          KeepHeaders: bool
          KeepRegions: bool
          KeepIfMatch: string list }

    let defaultSettings =
        { KeepDocs = false
          KeepHeaders = false
          KeepRegions = false
          KeepIfMatch = [] }

    let private isAnyCommentTrivia (t: SyntaxTrivia) =
        match t.Kind() with
        | SyntaxKind.SingleLineCommentTrivia
        | SyntaxKind.MultiLineCommentTrivia
        | SyntaxKind.SingleLineDocumentationCommentTrivia
        | SyntaxKind.MultiLineDocumentationCommentTrivia -> true
        | _ -> false

    type private TriviaRemovingRewriter(settings: StripSettings, headerKeepUptoIndex: int, firstToken: SyntaxToken) =
        inherit CSharpSyntaxRewriter()

        override _.VisitToken(token: SyntaxToken) =
            let filter (list: SyntaxTriviaList) (isLeading: bool) =
                if list.Count = 0 then
                    list
                else
                    let kept = System.Collections.Generic.List<SyntaxTrivia>(list.Count)

                    for idx = 0 to list.Count - 1 do
                        let t = list[idx]
                        let kind = t.Kind()

                        let keep =
                            (settings.KeepRegions
                             && (kind = SyntaxKind.RegionDirectiveTrivia
                                 || kind = SyntaxKind.EndRegionDirectiveTrivia))
                            ||
                            (settings.KeepDocs
                             && (kind = SyntaxKind.SingleLineDocumentationCommentTrivia
                                 || kind = SyntaxKind.MultiLineDocumentationCommentTrivia
                                 || kind = SyntaxKind.DocumentationCommentExteriorTrivia))
                            ||
                            (settings.KeepHeaders
                             && isLeading
                             && token.Equals(firstToken)
                             && idx <= headerKeepUptoIndex
                             && (isAnyCommentTrivia t
                                 || kind = SyntaxKind.WhitespaceTrivia
                                 || kind = SyntaxKind.EndOfLineTrivia
                                 || kind = SyntaxKind.DocumentationCommentExteriorTrivia))
                            ||
                            ((isAnyCommentTrivia t || kind = SyntaxKind.DocumentationCommentExteriorTrivia)
                             && settings.KeepIfMatch
                                |> List.exists (fun m ->
                                    not (String.IsNullOrEmpty m)
                                    && t.ToString().Contains(m, StringComparison.Ordinal)))

                        let remove =
                            match kind with
                            | SyntaxKind.SingleLineCommentTrivia
                            | SyntaxKind.MultiLineCommentTrivia -> true
                            | SyntaxKind.SingleLineDocumentationCommentTrivia
                            | SyntaxKind.MultiLineDocumentationCommentTrivia
                            | SyntaxKind.DocumentationCommentExteriorTrivia -> not settings.KeepDocs
                            | SyntaxKind.RegionDirectiveTrivia
                            | SyntaxKind.EndRegionDirectiveTrivia -> not settings.KeepRegions
                            | _ -> false

                        if not remove || keep then
                            kept.Add(t)

                    if kept.Count = list.Count then
                        list
                    else
                        SyntaxFactory.TriviaList(kept)

            let leading = filter token.LeadingTrivia true
            let trailing = filter token.TrailingTrivia false
            token.WithLeadingTrivia(leading).WithTrailingTrivia(trailing)

    let stripWith (settings: StripSettings) (source: string) =
        let options =
            CSharpParseOptions(
                languageVersion = LanguageVersion.Latest,
                documentationMode = DocumentationMode.Parse,
                kind = SourceCodeKind.Regular
            )

        let tree = CSharpSyntaxTree.ParseText(source, options)
        let root = tree.GetRoot()

        let mutable headerIdx = -1
        let first = root.GetFirstToken(true)

        if settings.KeepHeaders then
            let lt = first.LeadingTrivia
            let mutable i = 0
            let mutable sawComment = false

            while i < lt.Count do
                let k = lt[i].Kind()

                if k = SyntaxKind.WhitespaceTrivia || k = SyntaxKind.EndOfLineTrivia then
                    ()
                elif isAnyCommentTrivia lt[i] || k = SyntaxKind.DocumentationCommentExteriorTrivia then
                    sawComment <- true
                else
                    i <- lt.Count

                i <- i + 1

            if sawComment then
                headerIdx <- max -1 (i - 2)

        let rewriter = TriviaRemovingRewriter(settings, headerIdx, first)

        let newRoot =
            match rewriter.Visit(root) with
            | null -> root
            | node -> node

        newRoot.ToFullString()

    let strip (source: string) = stripWith defaultSettings source

module Cli =
    type Options =
        { ShowHelp: bool
          ShowVersion: bool
          InPlace: bool
          Recurse: bool
          OutputPath: string option
          BackupSuffix: string option
          Inputs: string list
          KeepDocs: bool
          KeepHeaders: bool
          KeepRegions: bool
          KeepIfMatch: string list }

    let empty =
        { ShowHelp = false
          ShowVersion = false
          InPlace = false
          Recurse = false
          OutputPath = None
          BackupSuffix = None
          Inputs = []
          KeepDocs = false
          KeepHeaders = false
          KeepRegions = false
          KeepIfMatch = [] }

    let helpText =
        """
Usage: fcc [options] [paths|-]

Strips C# comments and #region/#endregion from source while preserving strings.

Options:
  -h, --help           Show help and exit
  -V, --version        Show version and exit
  -i, --in-place       Overwrite input files with cleaned output
      --backup <sfx>   When using --in-place, also write backup as <file><sfx>
  -o, --output <path>  Write output to file (single input only)
  -r, --recurse        Recurse into directories to find *.cs files
      --keep-docs      Preserve XML doc comments (///, /** */)
      --keep-headers   Preserve the top-of-file license/header comment block
      --keep-regions   Preserve #region/#endregion directives
      --keep-if-match <text>  Preserve comments containing substring (repeatable)

Inputs:
  -                    Read from standard input (no --in-place/--output)
  path                 One or more files or, with -r, directories

Examples:
  fcc path/to/File.cs
  fcc path/to/Directory
"""

    let parse (argv: string array) =
        let rec loop i (o: Options) =
            if i >= argv.Length then
                o
            else
                match argv[i] with
                | "-h"
                | "--help" -> loop (i + 1) { o with ShowHelp = true }
                | "-V"
                | "--version" -> loop (i + 1) { o with ShowVersion = true }
                | "-i"
                | "--in-place" -> loop (i + 1) { o with InPlace = true }
                | "-r"
                | "--recurse" -> loop (i + 1) { o with Recurse = true }
                | "--keep-docs" -> loop (i + 1) { o with KeepDocs = true }
                | "--keep-headers" -> loop (i + 1) { o with KeepHeaders = true }
                | "--keep-regions" -> loop (i + 1) { o with KeepRegions = true }
                | "--keep-if-match" when i + 1 < argv.Length ->
                    loop
                        (i + 2)
                        { o with
                            KeepIfMatch = o.KeepIfMatch @ [ argv[i + 1] ] }
                | "--backup" when i + 1 < argv.Length ->
                    loop
                        (i + 2)
                        { o with
                            BackupSuffix = Some argv[i + 1] }
                | "-o"
                | "--output" when i + 1 < argv.Length -> loop (i + 2) { o with OutputPath = Some argv[i + 1] }
                | x -> loop (i + 1) { o with Inputs = o.Inputs @ [ x ] }

        loop 0 empty

    let toSettings (o: Options) : CommentStripper.StripSettings =
        { KeepDocs = o.KeepDocs
          KeepHeaders = o.KeepHeaders
          KeepRegions = o.KeepRegions
          KeepIfMatch = o.KeepIfMatch }

module Program =
    [<EntryPoint>]
    let main argv =
        let usage =
            "Usage: fcc <file-or-directory>\nStrips C# comments and regions in-place. No stdout."

        if argv.Length = 0 then
            eprintfn $"%s{usage}"
            1
        else
            let path = String.concat " " argv

            let stripFile (p: string) =
                let src = File.ReadAllText(p, Encoding.UTF8)
                let cleaned = CommentStripper.strip src
                File.WriteAllText(p, cleaned, UTF8Encoding(false))

            try
                if File.Exists(path) then
                    let ext = Path.GetExtension(path)
                    let isCs = String.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase)

                    if not isCs then
                        eprintfn $"fcc: not a .cs file: %s{path}"
                        2
                    else
                        stripFile path
                        0
                elif Directory.Exists(path) then
                    for f in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories) do
                        stripFile f

                    0
                else
                    eprintfn $"fcc: path not found: %s{path}"
                    1
            with ex ->
                eprintfn $"fcc: %s{ex.Message}"
                1
