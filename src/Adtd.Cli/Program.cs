using System.Net;
using Adtd.Collect;
using Adtd.Imaging;
using Adtd.Layout;
using Adtd.PowerPoint;
using Adtd.Model;
using Adtd.Model.Samples;

namespace Adtd.Cli;

public static class Program
{
    static readonly string Usage = """
        ADTD for PowerPoint: Active Directory topology to PowerPoint.

        Usage:
          adtd collect [--server <dc-or-domain>] [--user <DOMAIN\user>] [--no-addresses] [--out <file.json>] [--verbose]
              Reads the forest over LDAP and saves a snapshot (JSON) plus a log of every query (<file>.log).
              Runs as the signed-in user unless --user is given (the password is prompted for, never stored).
              Without --server, uses this machine's own domain.
          adtd sample <name> [--out <file.json>]
              Writes a made-up forest snapshot, for trying things out.
          adtd info <file.json>
              Summarises a snapshot.
          adtd template <file.potx> [--layout <name>] [--showcase <out.pptx>] [--icon <file.svg>] [--keep-slides]
              Lists the template's layouts and where diagrams would go on each. With --showcase, writes a test
              slide on the layout (default: the template's default layout) to check how the template takes it.
          adtd render <file.json | sample> --template <file.potx> [--layout <name>] [--out <file.pptx>]
                      [--icon <file.svg>] [--keep-slides] [--max-dcs <n>]
                      [--no-summary] [--no-domains] [--no-sites] [--no-domain-slides] [--no-site-slides]
                      [--no-subnets] [--no-addresses] [--replication] [--no-link-labels] [--no-other-trusts]
                      [--hide-empty-sites] [--consolidate-empty-sites] [--no-legend]
                      [--no-section-header] [--no-forest-header] [--no-sites-header]
              Draws a snapshot (or a sample forest) as slides on the template: a summary, the domains and trusts,
              the sites and site links, and a slide per domain and per site. Checks the layout and the file.
              Samples: {SampleList}.
              --consolidate-empty-sites draws empty sites that hang off the same site as one box (ADTD.Net's
              "Aggregate empty sites"). --no-section-header leaves out both section headers.
        """.Replace("{SampleList}", string.Join(", ", SampleForests.Names));

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help") { Console.WriteLine(Usage); return 0; }
        if (args[0] is "--version") { Console.WriteLine(ForestReader.Version); return 0; }
        try
        {
            var a = new Args(args.Skip(1));
            return args[0] switch
            {
                "collect" => await Collect(a),
                "sample" => Sample(a),
                "info" => Info(a),
                "template" => Template(a),
                "render" => Render(a),
                _ => Fail($"Unknown command '{args[0]}'.", 2),
            };
        }
        catch (UsageException ex) { return Fail(ex.Message, 2); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Fail(ex.Message, 1); }
    }

    static async Task<int> Collect(Args a)
    {
        var server = a.Option("--server");
        var user = a.Option("--user");
        var noAddresses = a.Flag("--no-addresses");
        var verbose = a.Flag("--verbose");
        var outPath = a.Option("--out");
        a.NoMore();

        if (server == null)
        {
            var located = DcLocator.Find();
            if (located == null) return Fail("This machine isn't in a domain (or no DC answered). Use --server <dc-or-domain>.", 1);
            server = located.Domain;
            Console.WriteLine($"Using this machine's domain {server} (DC {located.DomainController}).");
        }

        NetworkCredential? credential = null;
        if (user != null)
        {
            Console.Write($"Password for {user}: ");
            credential = new NetworkCredential(user, ReadPassword());
            if (user.Contains('\\')) { var p = user.Split('\\', 2); credential = new NetworkCredential(p[1], credential.Password, p[0]); }
        }

        outPath ??= $"adtd-{server}-{DateTime.Now:yyyyMMdd-HHmm}.json";
        outPath = Path.GetFullPath(outPath);
        var logPath = Path.ChangeExtension(outPath, ".log");
        await using var log = new StreamWriter(logPath) { AutoFlush = true };
        log.WriteLine($"adtd {ForestReader.Version} collect --server {server}{(user != null ? $" --user {user}" : "")}{(noAddresses ? " --no-addresses" : "")}");
        log.WriteLine($"Started {DateTimeOffset.Now:O} on {Environment.MachineName} as {Environment.UserDomainName}\\{Environment.UserName}");

        var progress = new SyncProgress(e =>
        {
            log.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {e.Level,-7} {e.Message}");
            if (e.Level == CollectLevel.Step) Console.WriteLine(e.Message);
            else if (e.Level == CollectLevel.Warning) WriteColored($"warning: {e.Message}", ConsoleColor.Yellow);
            else if (verbose) WriteColored(e.Message, ConsoleColor.DarkGray);
        });

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        try
        {
            var reader = new ForestReader(new LdapDirectoryFactory(new ConnectionOptions(credential)), new DnsIpResolver(), progress);
            var snapshot = await reader.ReadAsync(server, new CollectOptions { ResolveAddresses = !noAddresses }, cts.Token);
            SnapshotJson.Save(snapshot, outPath);
            Console.WriteLine($"Saved {outPath}");
            Console.WriteLine($"Log   {logPath}");
            return 0;
        }
        catch (OperationCanceledException) { log.WriteLine("Cancelled."); return Fail("Cancelled.", 1); }
        catch (Exception ex)
        {
            log.WriteLine($"FAILED: {ex}");
            return Fail($"{ex.Message}\nDetails are in {logPath}", 1);
        }
    }

    static int Sample(Args a)
    {
        var name = a.Positional() ?? throw new UsageException($"Which sample? {string.Join(", ", SampleForests.Names)}");
        var outPath = Path.GetFullPath(a.Option("--out") ?? $"{name}.json");
        a.NoMore();
        ForestSnapshot s;
        try { s = SampleForests.ByName(name); }
        catch (ArgumentException ex) { throw new UsageException(ex.Message); }
        SnapshotJson.Save(s, outPath);
        Console.WriteLine($"Saved {outPath}");
        return 0;
    }

    static int Info(Args a)
    {
        var path = a.Positional() ?? throw new UsageException("Which snapshot file?");
        a.NoMore();
        ForestSnapshot s;
        try { s = SnapshotJson.Load(path); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException) { return Fail($"Couldn't read {path}: {ex.Message}", 1); }

        string Dc(string? key) => key == null ? "-" : s.DomainControllers.FirstOrDefault(d => d.Key == key)?.Name ?? Dn.ValueAt(key, 1);
        Console.WriteLine($"Forest   {s.ForestName}  (collected {s.CollectedAt:yyyy-MM-dd HH:mm} from {s.CollectedFrom}, adtd {s.CollectorVersion})");
        Console.WriteLine($"Levels   forest {FunctionalLevels.Name(s.ForestFunctionalLevel)}; schema {FunctionalLevels.SchemaName(s.SchemaVersion)}");
        Console.WriteLine($"Masters  schema {Dc(s.SchemaMaster)}; domain naming {Dc(s.DomainNamingMaster)}");
        Console.WriteLine($"Counts   {s.Domains.Count} domains, {s.Sites.Count} sites, {s.DomainControllers.Count} DCs " +
                          $"({s.DomainControllers.Count(d => d.IsGlobalCatalog)} GC, {s.DomainControllers.Count(d => d.IsReadOnly)} RODC), " +
                          $"{s.Subnets.Count} subnets, {s.SiteLinks.Count} site links, {s.Connections.Count} connections");
        Console.WriteLine();
        foreach (var d in s.Domains)
        {
            Console.WriteLine($"  {d.DnsName} ({d.NetBiosName}) {FunctionalLevels.Name(d.FunctionalLevel)}{(d.Reachable ? "" : "  [unreachable]")}");
            Console.WriteLine($"    PDC {Dc(d.PdcMaster)}, RID {Dc(d.RidMaster)}, Infrastructure {Dc(d.InfrastructureMaster)}; " +
                              $"{s.DomainControllers.Count(x => x.Domain == d.DnsName)} DCs");
            foreach (var t in d.Trusts.Where(t => t.Kind is not (TrustKind.ParentChild or TrustKind.TreeRoot)))
                Console.WriteLine($"    {t.Kind} trust {t.Direction} with {t.Partner}");
        }
        if (s.Warnings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"{s.Warnings.Count} warning(s) from collection:");
            foreach (var w in s.Warnings) Console.WriteLine($"  {w}");
        }
        return 0;
    }

    static int Template(Args a)
    {
        var path = a.Positional() ?? throw new UsageException("Which template file?");
        var layoutName = a.Option("--layout");
        var showcase = a.Option("--showcase");
        var iconPath = a.Option("--icon");
        var keep = a.Flag("--keep-slides");
        a.NoMore();
        TemplateInfo t;
        try { t = TemplateReader.Read(path); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return Fail(ex.Message, 1); }

        Console.WriteLine($"{t.Name}: {t.SlideWidth / Emu.PerInch:0.###} × {t.SlideHeight / Emu.PerInch:0.###} in, {t.Layouts.Count} layouts, {t.ExistingSlides} slides");
        for (var m = 0; m < t.Themes.Count; m++)
        {
            var th = t.Themes[m];
            Console.WriteLine($"  Master {m}: theme \"{th.Name}\", fonts {th.MajorFont} / {th.MinorFont}");
            Console.WriteLine($"    accents {string.Join(" ", Enumerable.Range(0, 6).Select(i => th.Colors[SchemeSlot.Accent1 + i]))}; " +
                              $"text {th.Colors[SchemeSlot.Tx1]} on {th.Colors[SchemeSlot.Bg1]}");
        }
        var chosen = layoutName == null ? t.DefaultLayout : t.Find(layoutName);
        if (chosen == null) return Fail($"No layout called '{layoutName}'.", 1);
        Console.WriteLine();
        foreach (var l in t.Layouts)
            Console.WriteLine($"{(l == chosen ? "*" : " ")} {(t.Themes.Count > 1 ? $"[{l.MasterIndex}] " : "")}{l.Name,-32} {(l.Title == null ? "no title  " : "title     ")}{l.Content}  ({l.ContentSource})");
        if (showcase == null) return 0;

        IconAsset icon;
        try { icon = iconPath == null ? IconAsset.Default() : IconAsset.Load(iconPath); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return Fail(ex.Message, 1); }
        foreach (var w in icon.Warnings) WriteColored($"warning: {w}", ConsoleColor.Yellow);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(showcase))!);
        var plan = Showcase.Build(t.FrameOf(chosen), t.ThemeOf(chosen), icon.Aspect, chosen.Name);
        var report = PptxComposer.Write(showcase, path, chosen, plan, new IconImage(icon.Svg, icon.Png), new ComposeOptions { KeepExistingSlides = keep });
        foreach (var w in report.Warnings) WriteColored($"warning: {w}", ConsoleColor.Yellow);
        foreach (var e in report.ValidationErrors) WriteColored($"invalid: {e}", ConsoleColor.Red);
        Console.WriteLine($"Saved {Path.GetFullPath(showcase)} ({report.Slides} slide{(report.Slides == 1 ? "" : "s")} on {chosen.Name})");
        return report.ValidationErrors.Count == 0 ? 0 : 1;
    }

    static int Render(Args a)
    {
        var input = a.Positional() ?? throw new UsageException($"Which snapshot? A .json file, or a sample: {string.Join(", ", SampleForests.Names)}.");
        var templatePath = a.Option("--template") ?? throw new UsageException("--template <file.potx> is required.");
        var layoutName = a.Option("--layout");
        var outPath = a.Option("--out");
        var iconPath = a.Option("--icon");
        var maxDcs = a.Option("--max-dcs");
        var keep = a.Flag("--keep-slides");
        var options = new DeckOptions
        {
            Summary = !a.Flag("--no-summary"),
            DomainsOverview = !a.Flag("--no-domains"),
            SitesOverview = !a.Flag("--no-sites"),
            DomainDetails = !a.Flag("--no-domain-slides"),
            SiteDetails = !a.Flag("--no-site-slides"),
            Subnets = !a.Flag("--no-subnets"),
            Addresses = !a.Flag("--no-addresses"),
            Replication = a.Flag("--replication"),
            LinkLabels = !a.Flag("--no-link-labels"),
            OtherTrusts = !a.Flag("--no-other-trusts"),
            HideEmptySites = a.Flag("--hide-empty-sites"),
            ConsolidateEmptySites = a.Flag("--consolidate-empty-sites"),
            Legend = !a.Flag("--no-legend"),
        };
        var noHeaders = a.Flag("--no-section-header");
        options = options with { ForestSectionHeader = !a.Flag("--no-forest-header") && !noHeaders, SitesSectionHeader = !a.Flag("--no-sites-header") && !noHeaders };
        if (maxDcs != null)
            options = options with { MaxDcsPerSlide = int.TryParse(maxDcs, out var n) && n > 0 ? n : throw new UsageException("--max-dcs needs a whole number above 0.") };
        a.NoMore();

        ForestSnapshot snapshot;
        try
        {
            snapshot = File.Exists(input) ? SnapshotJson.Load(input)
                : SampleForests.Names.Contains(input, StringComparer.OrdinalIgnoreCase) ? SampleForests.ByName(input)
                : throw new UsageException($"No snapshot file {input}, and it isn't a sample ({string.Join(", ", SampleForests.Names)}).");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException) { return Fail($"Couldn't read {input}: {ex.Message}", 1); }

        TemplateInfo t;
        IconAsset icon;
        try
        {
            t = TemplateReader.Read(templatePath);
            icon = iconPath == null ? IconAsset.Default() : IconAsset.Load(iconPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return Fail(ex.Message, 1); }
        var layout = layoutName == null ? t.DefaultLayout : t.Find(layoutName);
        if (layout == null) return Fail($"No layout called '{layoutName}'. Run 'adtd template {templatePath}' to list them.", 1);
        foreach (var w in icon.Warnings) WriteColored($"warning: {w}", ConsoleColor.Yellow);

        var frame = t.FrameOf(layout);
        var theme = t.ThemeOf(layout);
        using var measurer = new SkiaTextMeasurer(theme);
        var plan = DeckPlanner.Plan(snapshot, frame, theme, measurer, icon.Aspect, options);
        var problems = PlanChecks.Check(plan, frame, measurer, options.MinFontPt);

        outPath = Path.GetFullPath(outPath ?? Path.GetFileNameWithoutExtension(input) + ".pptx");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        var report = PptxComposer.Write(outPath, templatePath, layout, plan, new IconImage(icon.Svg, icon.Png), new ComposeOptions { KeepExistingSlides = keep, SectionLayout = t.SectionLayoutFor(layout) });
        foreach (var w in report.Warnings) WriteColored($"warning: {w}", ConsoleColor.Yellow);
        foreach (var p in problems) WriteColored($"layout: {p}", ConsoleColor.Red);
        foreach (var e in report.ValidationErrors) WriteColored($"invalid: {e}", ConsoleColor.Red);
        Console.WriteLine($"Saved {outPath} ({report.Slides} slide{(report.Slides == 1 ? "" : "s")} on {layout.Name})");
        return report.ValidationErrors.Count == 0 && problems.Count == 0 ? 0 : 1;
    }

    static string ReadPassword()
    {
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; }
            else if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
        }
        Console.WriteLine();
        return sb.ToString();
    }

    static void WriteColored(string message, ConsoleColor color)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = old;
    }

    static int Fail(string message, int code)
    {
        WriteColored(message, ConsoleColor.Red);
        if (code == 2) Console.WriteLine("Run 'adtd --help' for usage.");
        return code;
    }

    sealed class SyncProgress(Action<CollectEvent> report) : IProgress<CollectEvent>
    {
        readonly object _gate = new();
        public void Report(CollectEvent value) { lock (_gate) report(value); }
    }
}

sealed class UsageException(string message) : Exception(message);

/// <summary>Tiny argument parser: --flag, --option value, and positionals, in any order.</summary>
sealed class Args(IEnumerable<string> args)
{
    readonly List<string> _rest = args.ToList();

    public bool Flag(string name) => _rest.RemoveAll(x => x == name) > 0;

    public string? Option(string name)
    {
        var i = _rest.IndexOf(name);
        if (i < 0) return null;
        if (i + 1 >= _rest.Count || _rest[i + 1].StartsWith("--")) throw new UsageException($"{name} needs a value.");
        var v = _rest[i + 1];
        _rest.RemoveRange(i, 2);
        return v;
    }

    public string? Positional()
    {
        var i = _rest.FindIndex(x => !x.StartsWith("--"));
        if (i < 0) return null;
        var v = _rest[i];
        _rest.RemoveAt(i);
        return v;
    }

    public void NoMore()
    {
        if (_rest.Count > 0) throw new UsageException($"Unexpected: {string.Join(' ', _rest)}");
    }
}
