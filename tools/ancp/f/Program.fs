open System
open System.IO
open System.Text
open System.IO.Compression
open System.Xml.Linq
open System.Diagnostics

let normalize (word: string) = word.ToLowerInvariant()

let stripComments (text: string) =
    let sb = StringBuilder(text.Length)
    let mutable i = 0
    let len = text.Length

    let startsWith (s: string) =
        if i + s.Length <= len then
            let mutable k = 0
            let mutable eq = true

            while eq && k < s.Length do
                if text[i + k] <> s[k] then
                    eq <- false

                k <- k + 1

            eq
        else
            false

    let mutable inHashLine = false
    let mutable inBlockHtml = false

    while i < len do
        if inHashLine then
            if text[i] = '\n' then
                sb.Append('\n') |> ignore
                inHashLine <- false

            i <- i + 1
        elif inBlockHtml then
            if startsWith "-->" then
                i <- i + 3
                inBlockHtml <- false
            else
                i <- i + 1
        else if startsWith "<!--" then
            inBlockHtml <- true
            i <- i + 4
        elif text[i] = '#' then
            inHashLine <- true
            i <- i + 1
        else
            sb.Append(text[i]) |> ignore
            i <- i + 1

    sb.ToString()

let tokenize (text: string) =
    let isWordChar (ch: char) =
        let cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory ch

        match cat with
        | System.Globalization.UnicodeCategory.UppercaseLetter
        | System.Globalization.UnicodeCategory.LowercaseLetter
        | System.Globalization.UnicodeCategory.TitlecaseLetter
        | System.Globalization.UnicodeCategory.ModifierLetter
        | System.Globalization.UnicodeCategory.OtherLetter
        | System.Globalization.UnicodeCategory.DecimalDigitNumber
        | System.Globalization.UnicodeCategory.LetterNumber
        | System.Globalization.UnicodeCategory.OtherNumber
        | System.Globalization.UnicodeCategory.NonSpacingMark
        | System.Globalization.UnicodeCategory.SpacingCombiningMark
        | System.Globalization.UnicodeCategory.EnclosingMark -> true
        | _ -> false

    let isApostrophe ch = ch = '\'' || ch = '\u2019'

    let sb = StringBuilder()
    let results = System.Collections.Generic.List<string>()

    let inline flush () =
        if sb.Length > 0 then
            results.Add(normalize (sb.ToString()))
            sb.Clear() |> ignore

    for ch in text do
        if isWordChar ch || isApostrophe ch then
            sb.Append(ch) |> ignore
        else
            flush ()

    flush ()
    results |> Seq.filter (fun w -> w.Length > 0) |> Seq.toList

let readDocx (path: string) =
    use fs = File.OpenRead(path)
    use za = new ZipArchive(fs, ZipArchiveMode.Read)
    let entry = za.GetEntry("word/document.xml")

    if isNull entry then
        ""
    else
        use s = entry.Open()
        let xdoc = XDocument.Load(s)

        let ns =
            XNamespace.Get "http://schemas.openxmlformats.org/wordprocessingml/2006/main"

        xdoc.Descendants(ns + "p")
        |> Seq.map (fun p -> p.Descendants(ns + "t") |> Seq.map _.Value |> String.concat "")
        |> String.concat "\n"

let tryReadPdf (path: string) =
    try
        match Environment.GetEnvironmentVariable("ANCP_PDFTOTEXT") with
        | null
        | "" ->
            let p = new Process()

            p.StartInfo <-
                ProcessStartInfo(
                    FileName = "pdftotext",
                    Arguments = $"-q -enc UTF-8 -nopgbrk \"{path}\" -",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                )

            let started = p.Start()

            if not started then
                None
            else
                let output = p.StandardOutput.ReadToEnd()
                p.WaitForExit()
                if p.ExitCode = 0 then Some output else None
        | v when v.Equals("INLINE", StringComparison.OrdinalIgnoreCase) ->
            let txt = File.ReadAllText(path, Encoding.UTF8)
            Some txt
        | v when v.Equals("FAIL", StringComparison.OrdinalIgnoreCase) -> None
        | customExe ->
            let p = new Process()

            p.StartInfo <-
                ProcessStartInfo(
                    FileName = customExe,
                    Arguments = $"-q -enc UTF-8 -nopgbrk \"{path}\" -",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                )

            let started = p.Start()

            if not started then
                None
            else
                let output = p.StandardOutput.ReadToEnd()
                p.WaitForExit()
                if p.ExitCode = 0 then Some output else None
    with _ ->
        None

let readInput (args: string array) =
    match args |> Array.toList with
    | [ path ] ->
        let ext = Path.GetExtension(path).ToLowerInvariant()

        match ext with
        | ".docx" -> readDocx path
        | ".pdf" ->
            match tryReadPdf path with
            | Some text -> text
            | None -> failwith "Failed to extract PDF text. Install 'pdftotext' or pipe text via stdin."
        | _ -> File.ReadAllText(path)
    | _ -> failwith "Usage: f [--strip|--unique] <file>"

[<EntryPoint>]
let main argv =
    let unique = argv |> Array.exists ((=) "--unique")
    let stripOnly = argv |> Array.exists ((=) "--strip")
    let showHelp = argv |> Array.exists (fun a -> a = "--help" || a = "-h")
    let inputs = argv |> Array.filter (fun a -> not (a.StartsWith("--")))

    if showHelp then
        printfn "Usage: f [--strip|--unique] <file>"
        printfn "  --strip   Output text with #/HTML comments removed"
        printfn "  --unique  Print unique word count (default is total)"
        printfn "  <file>    .txt/.md/.docx/.pdf"
        printfn "Notes: PDF extraction uses 'pdftotext' if available."
        0
    else
        try
            let raw = readInput inputs
            let stripped = stripComments raw

            if stripOnly then
                printf $"%s{stripped}"
                0
            else
                let tokens = tokenize stripped

                let count =
                    if unique then
                        tokens |> List.distinct |> List.length
                    else
                        tokens.Length

                printfn $"%d{count}"
                0
        with
        | :? FileNotFoundException as ex ->
            eprintfn $"Error: %s{ex.Message}"
            1
        | :? UnauthorizedAccessException as ex ->
            eprintfn $"Error: %s{ex.Message}"
            1
        | ex ->
            eprintfn $"Error: %s{ex.Message}"
            1
