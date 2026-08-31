using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;

namespace ProposalStudio.Services
{
    /// <summary>
    /// Data for the House of Pianos private proposal PDF (matches the 6-page template).
    /// </summary>
    public record ProposalPdfModel(
        string Reference,
        string ClientName,
        string AdvisorName,
        string? AdvisorTitle,
        string BrandName,
        string? BrandBlurb,
        string Model,
        string? Tagline,
        string? Blurb,
        string? Finish,
        string Currency,
        decimal? PriceTotal,
        int ValidityDays,
        string VatMode,
        string[] Features,
        string[] Included,
        string[] Excluded,
        string[] Finishes,
        IReadOnlyDictionary<string, string> Dimensions,
        string? AvailabilityNote,
        string BusinessName,
        string? BusinessBlurb,
        string Phone,
        string Website,
        string Instagram,
        string AddressLine,
        string? ProductImagePath,
        DateTimeOffset IssuedAt,
        Guid? ProductId = null,
        int Qty = 1,
        IReadOnlyList<ProposalAddonLine>? Addons = null
    );

    public class ProposalPdfService
    {
        // Palette sampled directly from the House of Pianos template deck
        private const string Ink = "#0B0B0C";        // dark page background
        private const string Paper = "#FFFFFF";      // light page background
        private const string Cream = "#F6F1E9";      // headings & values on dark
        private const string Gold = "#B29064";       // eyebrows, rules, footer marks
        private const string GoldLight = "#CBA978";  // italic accents & price
        private const string Charcoal = "#1B1B1B";   // headings on light
        private const string BodyInk = "#33312E";    // body copy on light
        private const string Muted = "#726C64";      // captions on light
        private const string MutedDark = "#BDB6A9";  // captions & body on dark
        private const string Faint = "#7C766C";      // cover meta line
        private const string ExcludeInk = "#55514B"; // exclusions list
        private const string CardLine = "#E0D6C5";   // gallery card border
        private const string RowLine = "#23211E";    // specification table rules

        // Same families as the House of Pianos template PDF
        private const string FontDisplay = "HopBookman";      // Bookman Old Style
        private const string FontDisplayBold = "HopBookmanBold";
        private const string FontBody = "HopCambria";         // Cambria / Caladea
        private const string FontBodyItalic = "HopCambriaItalic";
        private const string FontUi = "HopArial";             // Arial
        private const string FontUiBold = "HopArialBold";

        /// <summary>Template page size: 960×540 pt (~16:9 landscape).</summary>
        private static readonly PageSize Deck = new(960, 540);

        // Template geometry (points, measured off the reference deck)
        private const float PageW = 960f;
        private const float PageH = 540f;
        private const float MarginX = 64.8f;
        private const float FooterY = 513.9f;

        /// <summary>Letter spacing as a fraction of font size, matching the template's tracked caps.</summary>
        private const float Track = 0.30f;

        private static bool _fontsReady;

        private readonly ProductImageStore? _images;

        public ProposalPdfService(ProductImageStore? images = null)
        {
            _images = images;
            QuestPDF.Settings.License = LicenseType.Community;
            EnsureTemplateFonts();
        }

        private static void EnsureTemplateFonts()
        {
            if (_fontsReady) return;
            lock (typeof(ProposalPdfService))
            {
                if (_fontsReady) return;

                var dirs = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "Fonts"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Fonts"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts")
                };

                // Real Bookman Old Style if licensed and dropped into Fonts/, else the free URW cut.
                // The BCD* files extracted from the template are subsets and would drop glyphs.
                RegisterNamed(FontDisplay, dirs,
                    "BOOKOS.TTF", "BookmanOldStyle.ttf", "Bookman Old Style.ttf",
                    "URWBookman-Light.otf");
                RegisterNamed(FontDisplayBold, dirs,
                    "BOOKOSB.TTF", "BookmanOldStyle-Bold.ttf", "Bookman Old Style Bold.ttf",
                    "URWBookman-Demi.otf");
                // Caladea is metric-compatible with Cambria and free to redistribute.
                RegisterNamed(FontBody, dirs, "cambria.ttf", "Caladea-Regular.ttf");
                RegisterNamed(FontBodyItalic, dirs, "cambriai.ttf", "Caladea-Italic.ttf");
                RegisterNamed(FontUi, dirs, "arial.ttf", "Arimo-Regular.ttf");
                RegisterNamed(FontUiBold, dirs, "arialbd.ttf", "Arimo-Bold.ttf");

                _fontsReady = true;
            }
        }

        private static void RegisterNamed(string family, string[] dirs, params string[] fileNames)
        {
            foreach (var dir in dirs)
            {
                foreach (var file in fileNames)
                {
                    var path = Path.Combine(dir, file);
                    if (!System.IO.File.Exists(path)) continue;
                    try
                    {
                        using var stream = System.IO.File.OpenRead(path);
                        FontManager.RegisterFontWithCustomName(family, stream);
                        return;
                    }
                    catch
                    {
                        // try next candidate
                    }
                }
            }
        }

        /// <summary>
        /// Dispatches to the layout registered for <paramref name="rendererKey"/>.
        /// Unknown keys fall back to <see cref="ProposalRendererCatalog.PianoLuxury"/>.
        /// </summary>
        public byte[] GenerateFor(
            string? rendererKey,
            ProposalPdfModel model,
            PdfPalette? palette = null,
            string? logoFile = null)
        {
            return ProposalRendererCatalog.Normalize(rendererKey) switch
            {
                ProposalRendererCatalog.PianoLuxury => Generate(model, palette, logoFile),
                _ => Generate(model, palette, logoFile)
            };
        }

        /// <summary>Piano — Luxury six-page deck. Invoked via <see cref="GenerateFor"/>.</summary>
        public byte[] Generate(ProposalPdfModel model, PdfPalette? palette = null, string? logoFile = null)
        {
            var p = palette ?? PdfPalette.Defaults;
            var Ink = p.Ink;
            // Light pages stay white; kit "paper" is a cream that muddies photographs.
            var Paper = "#FFFFFF";
            var Cream = p.Cream;
            var Gold = p.Gold;
            var GoldLight = p.GoldLight;
            var Charcoal = p.Charcoal;
            var BodyInk = p.BodyInk;
            var Muted = p.Muted;
            var MutedDark = p.MutedDark;
            var ExcludeInk = p.ExcludeInk;
            var CardLine = p.CardLine;
            var RowLine = p.RowLine;

            var addons = model.Addons ?? Array.Empty<ProposalAddonLine>();
            var qty = model.Qty < 1 ? 1 : model.Qty;
            var vatHint = VatCaption(model);

            var finishLine = string.IsNullOrWhiteSpace(model.Finish) ? "—" : model.Finish!;
            var modelFinishHeader = qty > 1
                ? $"{model.Model} · {finishLine} · Qty {qty}".ToUpperInvariant()
                : $"{model.Model} · {finishLine}".ToUpperInvariant();
            var issuedLine = $"{model.Reference}  ·  {FormatIssuedDate(model.IssuedAt)}";
            var clientSize = FitSize(model.ClientName, 470f, 38.2f, 18f, EmDisplayBold);
            var instrumentSize = FitSize($"{model.BrandName} — {model.Model}", 470f, 20.9f, 13f, EmDisplay);
            var coverTag = string.IsNullOrWhiteSpace(model.Tagline) ? null : $"“{TrimQuotes(model.Tagline!)}”";
            var coverTagSize = coverTag == null ? 13.7f : FitSize(coverTag, 470f, 13.7f, 10f, EmItalic);
            var pageTagSize = string.IsNullOrWhiteSpace(model.Tagline)
                ? 15.1f
                : FitSize(model.Tagline!, 445f, 15.1f, 11f, EmItalic);
            var includes = MergeIncludes(model.Included, addons);
            var contactCompact = string.Join(
                "    ·    ",
                new[] { model.Phone, model.Website, model.Instagram }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));

            var dimRows = model.Dimensions.Count > 0
                ? OrderDimensions(model.Dimensions)
                : new List<KeyValuePair<string, string>>
                {
                    new("Length", "—"), new("Width", "—"), new("Weight", "—")
                };

            var finishesLine = model.Finishes.Length > 0
                ? string.Join("   ·   ", model.Finishes)
                : "Quoted on request";

            var features = model.Features.Length > 0
                ? model.Features
                : new[] { $"Handcrafted by {model.BrandName}" };

            var excluded = model.Excluded;

            var gallery = GalleryPaths(model);
            var logo = BrandAsset(logoFile) ?? BrandAsset(BrandStyleService.DefaultLogoFile);
            var star = BrandAsset("bullet-star.png");
            var check = BrandAsset("bullet-check.png");
            var dash = BrandAsset("bullet-dash.png");

            var disclaimer =
                $"Prices are quoted in {CurrencyName(model.Currency)} and remain valid for {model.ValidityDays} days " +
                $"from the date of this proposal. Finishes other than {finishLine} are quoted on request.";

            return Document.Create(container =>
            {
                // ───────────── 01 Cover ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Ink, Cream);
                    page.Content().Layers(l =>
                    {
                        Base(l);

                        if (logo != null)
                            At(l, MarginX, 43.2f).Width(58.3f).Image(logo);

                        AtRight(l, 891.4f, 52.1f).Text("A PRIVATE PROPOSAL")
                            .Style(Caps(10.8f, Gold));

                        AtRight(l, 895.8f, 75.0f)
                            .Text($"{model.BrandName}  ·  Authorised Representative, United Arab Emirates")
                            .Style(Serif(FitSize(
                                $"{model.BrandName}  ·  Authorised Representative, United Arab Emirates",
                                420f, 10.8f, 8.5f, EmItalic), MutedDark));

                        AtRight(l, 895.8f, 96.0f)
                            .Text(issuedLine)
                            .Style(Sans(9.4f, MutedDark));

                        Hero(At(l, 571.0f, 148.0f).Width(356.4f).Height(330.0f), model, Ink, Gold, Cream, Charcoal, Paper);

                        AtCaps(l, MarginX, 221.8f, 12.3f).Text("PREPARED EXCLUSIVELY FOR")
                            .Style(Caps(12.3f, Gold));

                        At(l, MarginX, 245.7f, 470f).Text(model.ClientName)
                            .Style(Display(clientSize, Cream, bold: true));

                        At(l, 68.8f, 324.4f).Width(151.2f).LineHorizontal(1).LineColor(Gold);

                        AtCaps(l, MarginX, 346.4f, 10.8f).Text("THE INSTRUMENT").Style(Caps(10.8f, Gold));

                        At(l, MarginX, 360.4f, 470f).Text($"{model.BrandName} — {model.Model}")
                            .Style(Display(instrumentSize, Cream));

                        if (coverTag != null)
                            At(l, MarginX, 406.2f, 470f).Text(coverTag)
                                .Style(Italic(coverTagSize, GoldLight));

                        At(l, MarginX, 502.9f).Text(contactCompact).Style(Contact(9.4f, MutedDark));
                    });
                });

                // ───────────── 02 The Instrument ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Paper, BodyInk);
                    page.Content().Layers(l =>
                    {
                        Base(l);
                        SectionHead(l, model.BrandName.ToUpperInvariant(), model.Model, 40.4f, Charcoal, 75.0f, Gold);

                        if (!string.IsNullOrWhiteSpace(model.Tagline))
                            At(l, MarginX, 148.2f, 445f).Text(model.Tagline!).Style(Italic(pageTagSize, Gold));

                        At(l, MarginX, 180.0f, 442f).Column(c =>
                        {
                            foreach (var para in Paragraphs(ClipCopy(model.Blurb, ProposalCopyLimits.BlurbMaxChars)))
                                c.Item().PaddingBottom(14).Text(para)
                                    .Style(Serif(13.7f, BodyInk)).LineHeight(1.38f);
                        });

                        At(l, MarginX, 360.1f, 440f).Row(r =>
                        {
                            var shown = features.Take(4).ToArray();
                            var half = (int)Math.Ceiling(shown.Length / 2.0);
                            r.ConstantItem(219.6f).Column(c => Bullets(
                                c, shown.Take(half), star, 11.5f, 19.5f, 43.2f, 12.2f, Charcoal, 1.1f, Gold));
                            r.RelativeItem().Column(c => Bullets(
                                c, shown.Skip(half), star, 11.5f, 19.5f, 43.2f, 12.2f, Charcoal, 1.1f, Gold));
                        });

                        Hero(At(l, 596.2f, 128.0f).Width(313.2f).Height(286.0f), model, Paper, Gold, Cream, Charcoal, Paper);

                        At(l, 596.2f, 428.8f, 313.2f).AlignCenter()
                            .Text(qty > 1
                                ? $"{model.Model}  ·  {finishLine}  ·  Qty {qty}"
                                : $"{model.Model}  ·  {finishLine}")
                            .Style(Italic(FitSize(
                                qty > 1
                                    ? $"{model.Model}  ·  {finishLine}  ·  Qty {qty}"
                                    : $"{model.Model}  ·  {finishLine}",
                                313f, 10.8f, 8.5f, EmItalic), Muted));

                        Footer(l, "PRIVATE PROPOSAL", 2, Muted, Gold);
                    });
                });

                // ───────────── 03 Gallery ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Paper, BodyInk);
                    page.Content().Layers(l =>
                    {
                        Base(l);
                        SectionHead(l, "GALLERY", "Views & Details", 33.9f, Charcoal, 75.9f, Gold);

                        At(l, MarginX, 143.2f, 830f)
                            .Text("A closer study of the finish, form, and detailing of the instrument reserved for you.")
                            .Style(Italic(13.0f, Muted));

                        GalleryCard(At(l, 65.2f, 184.0f).Width(399.6f).Height(284.5f), gallery[0], model, Gold);
                        GalleryCard(At(l, 486.4f, 184.0f).Width(403.2f).Height(128.0f), gallery[1], model, Gold,
                            fillWidth: 403.2f, fillHeight: 128.0f);
                        GalleryCard(At(l, 486.4f, 338.0f).Width(403.2f).Height(128.0f), gallery[2], model, Gold,
                            fillWidth: 403.2f, fillHeight: 128.0f);

                        At(l, 65.2f, 474.2f, 399.6f).AlignCenter()
                            .Text("01  ·  Full profile").Style(Caption(9.4f, Muted));

                        At(l, 486.4f, 314.5f, 403.2f).AlignCenter()
                            .Text("02  ·  Action & soundboard").Style(Caption(9.4f, Muted));

                        At(l, 486.4f, 474.2f, 403.2f).AlignCenter()
                            .Text("03  ·  Keyboard & fallboard").Style(Caption(9.4f, Muted));

                        Footer(l, "PRIVATE PROPOSAL", 3, Muted, Gold);
                    });
                });

                // ───────────── 04 Specifications ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Ink, Cream);
                    page.Content().Layers(l =>
                    {
                        Base(l);
                        SectionHead(l, "SPECIFICATIONS", model.Model, 31.7f, Cream, 73.1f, Gold);

                        At(l, MarginX, 138.9f, 445f).Text(CategoryLine(model, Dimension(dimRows, "length")))
                            .Style(Italic(13.0f, GoldLight));

                        AtCaps(l, MarginX, 188.4f, 10.8f).Text("DIMENSIONS & WEIGHT").Style(Caps(10.8f, Gold));

                        At(l, MarginX, 227.9f, 396.4f).Column(c =>
                        {
                            foreach (var (label, value) in dimRows.Take(4))
                            {
                                c.Item().PaddingBottom(9.4f).BorderBottom(1).BorderColor(RowLine).Row(r =>
                                {
                                    r.ConstantItem(151.3f).Text(TitleCase(label)).Style(Sans(11.5f, MutedDark));
                                    r.RelativeItem().Text(value).Style(Serif(13.7f, Cream));
                                });
                                c.Item().Height(15.2f);
                            }
                        });

                        AtCaps(l, 515.1f, 188.4f, 10.8f).Text("CRAFTSMANSHIP").Style(Caps(10.8f, Gold));

                        At(l, 514.8f, 224.7f, 380f).Column(c => Bullets(
                            c, features.Take(7), star, 10.1f, 19.7f, 33.8f, 13.0f, Cream, -0.4f, Gold));

                        AtCaps(l, MarginX, 451.6f, 10.8f).Text("AVAILABLE FINISHES").Style(Caps(10.8f, Gold));
                        At(l, MarginX, 474.1f, 830f).Column(c =>
                        {
                            c.Item().Text(finishesLine).Style(Serif(
                                FitSize(finishesLine, 830f, 12.2f, 9.4f, EmItalic), MutedDark));
                            if (!string.IsNullOrWhiteSpace(model.AvailabilityNote))
                                c.Item().PaddingTop(6).Text(model.AvailabilityNote)
                                    .Style(Italic(10.8f, MutedDark));
                        });

                        Footer(l, "SPECIFICATIONS", 4, MutedDark, Gold);
                    });
                });

                // ───────────── 05 The Investment ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Paper, BodyInk);
                    page.Content().Layers(l =>
                    {
                        Base(l);
                        SectionHead(l, "THE INVESTMENT", "Your Investment", 33.9f, Charcoal, 76.1f, Gold);

                        var addonPitch = 13.0f;
                        var cardExtra = addons.Count * addonPitch;
                        var cardH = 126.1f + cardExtra;
                        var excludeShift = cardExtra;

                        // Drawn as SVG because QuestPDF 2024.12 has no corner radius on Background().
                        At(l, MarginX, 151.2f).Width(385.2f).Height(cardH).Svg(
                            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 385 " +
                            cardH.ToString("0.###", CultureInfo.InvariantCulture) + "'>" +
                            $"<rect width='385' height='{cardH.ToString("0.###", CultureInfo.InvariantCulture)}' rx='5.4' fill='{Ink}'/></svg>");

                        At(l, MarginX, 151.2f).Width(385.2f).Height(cardH)
                            .PaddingLeft(20.2f).PaddingRight(20.2f).PaddingTop(18.0f).Column(c =>
                            {
                                var headerSize = FitSize(modelFinishHeader, 344f, 9.4f, 7.2f, EmCaps);
                                c.Item().Text(modelFinishHeader).Style(Caps(headerSize, MutedDark));
                                c.Item().PaddingTop(2).Text(t =>
                                {
                                    if (model.PriceTotal is > 0)
                                    {
                                        var figure = ProposalQuote.FormatMoney(model.PriceTotal.Value);
                                        var priceSize = FitSize(
                                            $"{model.Currency} {figure}", 344f, 33.9f, 18f, EmDisplayBold);
                                        t.Span($"{model.Currency} ").Style(Display(Math.Max(14f, priceSize * 0.6f), GoldLight, bold: true));
                                        t.Span(figure)
                                            .Style(Display(priceSize, GoldLight, bold: true));
                                    }
                                    else
                                    {
                                        t.Span("On request").Style(Display(26f, GoldLight, bold: true));
                                    }
                                });
                                foreach (var addon in addons)
                                {
                                    var line = addon.Amount > 0
                                        ? $"{addon.Name}  ·  {model.Currency} {ProposalQuote.FormatMoney(addon.Amount)}"
                                        : addon.Name;
                                    c.Item().PaddingTop(3).Text(line)
                                        .Style(Sans(FitSize(line, 344f, 9.0f, 7.0f, 0.50f), MutedDark));
                                }
                                c.Item().PaddingTop(addons.Count > 0 ? 6 : 12)
                                    .Text($"Recommended retail  ·  {vatHint}")
                                    .Style(Italic(10.1f, MutedDark));
                            });

                        var excludeHeadY = 305.9f + excludeShift;
                        var excludeListY = 337.0f + excludeShift;
                        var excludeAvail = Math.Max(36f, 464f - excludeListY);
                        var excludePitch = ListPitch(excluded.Length, excludeAvail, 33.2f);
                        var excludeFont = Math.Clamp(excludePitch * (12.3f / 33.2f), 8.5f, 12.3f);

                        AtCaps(l, MarginX, excludeHeadY, 10.8f).Text("NOT INCLUDED").Style(Caps(10.8f, Gold));
                        At(l, 66.2f, excludeListY, 385f).Column(c => Bullets(
                            c, excluded, dash, 11.6f, 20.2f, excludePitch, excludeFont, ExcludeInk, 1.1f, Gold, 348f));

                        var includePitch = ListPitch(includes.Length, 277f, 36.0f);
                        var includeFont = Math.Clamp(includePitch * (13.0f / 36.0f), 8.5f, 13.0f);
                        AtCaps(l, 486.2f, 156.0f, 10.8f).Text("YOUR INVESTMENT INCLUDES").Style(Caps(10.8f, Gold));
                        At(l, 487.4f, 187.2f, 405f).Column(c => Bullets(
                            c, includes, check, 14.4f, 25.5f, includePitch, includeFont, Charcoal, 2.5f, Gold, 365f));

                        At(l, MarginX, 472.2f, 830f).Text(disclaimer).Style(Italic(10.8f, Muted));

                        Footer(l, "THE INVESTMENT", 5, Muted, Gold);
                    });
                });

                // ───────────── 06 Closing ─────────────
                container.Page(page =>
                {
                    ApplyDeck(page, Ink, Cream);
                    page.Content().Layers(l =>
                    {
                        Base(l);

                        if (logo != null)
                            AtCenter(l, 57.6f, 87.1f).Image(logo);

                        AtCenter(l, 216.0f, 700f).AlignCenter()
                            .Text("“A pianist grows as good as the piano.”")
                            .Style(Italic(18.7f, GoldLight));

                        AtCenter(l, 255.7f, 585f).Text(
                                "We would be honoured to welcome you to Steinway Hall Dubai to experience this " +
                                "instrument in person — and to accompany you for many years beyond its delivery.")
                            .Style(Serif(13.7f, MutedDark)).LineHeight(1.42f).AlignCenter();

                        AtCenter(l, 338.8f, 128.9f).LineHorizontal(1).LineColor(Gold);

                        AtCenter(l, 361.0f, 700f).AlignCenter()
                            .Text("STEINWAY HALL DUBAI").Style(Caps(12.2f, Gold));

                        AtCenter(l, 384.2f, 700f).AlignCenter()
                            .Text(model.AddressLine).Style(Serif(12.3f, Cream));

                        AtCenter(l, 408.4f, 700f).AlignCenter()
                            .Text(contactCompact).Style(Contact(10.8f, MutedDark));

                        AtCenter(l, 460.9f, 700f).AlignCenter().Text(t =>
                        {
                            t.Span("Prepared by  ").Style(Serif(13.0f, MutedDark));
                            t.Span(model.AdvisorName).Style(Serif(
                                FitSize($"Prepared by  {model.AdvisorName}", 700f, 13.0f, 10f, EmItalic), Cream));
                        });

                        AtCenter(l, 487.0f, 700f).AlignCenter()
                            .Text($"{model.AdvisorTitle ?? "Sales Advisor"}  ·  {model.BusinessName}")
                            .Style(Sans(9.4f, Muted).LetterSpacing(0.12f));
                    });
                });
            }).GeneratePdf();
        }

        // ───────────────────────── layout primitives ─────────────────────────

        /// <summary>Full-bleed sizing layer; every other layer is absolutely positioned on top.</summary>
        private static void Base(LayersDescriptor layers) =>
            layers.PrimaryLayer().Width(PageW).Height(PageH).Background(Colors.Transparent);

        private static IContainer At(LayersDescriptor layers, float x, float y) =>
            layers.Layer().PaddingLeft(x).PaddingTop(y).AlignTop().AlignLeft();

        private static IContainer At(LayersDescriptor layers, float x, float y, float width) =>
            layers.Layer().PaddingLeft(x).PaddingTop(y).AlignTop().Width(width);

        private static IContainer AtRight(LayersDescriptor layers, float rightEdge, float y) =>
            layers.Layer().PaddingRight(PageW - rightEdge).PaddingTop(y).AlignTop().AlignRight();

        private static IContainer AtCenter(LayersDescriptor layers, float y, float width) =>
            layers.Layer().PaddingTop(y).AlignTop().AlignCenter().Width(width);

        /// <summary>
        /// Tracked caps start half a letter-space in from the block edge, so pull the block
        /// back by that much to land the first glyph on the template's left margin.
        /// </summary>
        private static IContainer AtCaps(LayersDescriptor layers, float x, float y, float size) =>
            At(layers, x - size * Track / 2f, y);

        private static TextStyle Caps(float size, string color) => TextStyle.Default
            .FontFamily(FontUiBold).FontSize(size).FontColor(color).LetterSpacing(Track);

        private static TextStyle Sans(float size, string color) => TextStyle.Default
            .FontFamily(FontUi).FontSize(size).FontColor(color);

        /// <summary>Phone / web / handle lines, lightly tracked as in the template.</summary>
        private static TextStyle Contact(float size, string color) => Sans(size, color).LetterSpacing(0.105f);

        /// <summary>Gallery plate captions, tracked a little wider than body sans.</summary>
        private static TextStyle Caption(float size, string color) => Sans(size, color).LetterSpacing(0.2f);

        private static TextStyle Serif(float size, string color) => TextStyle.Default
            .FontFamily(FontBody).FontSize(size).FontColor(color);

        private static TextStyle Italic(float size, string color) => TextStyle.Default
            .FontFamily(FontBodyItalic).FontSize(size).FontColor(color);

        private static TextStyle Display(float size, string color, bool bold = false) => TextStyle.Default
            .FontFamily(bold ? FontDisplayBold : FontDisplay).FontSize(size).FontColor(color);

        /// <summary>
        /// Gold eyebrow plus Bookman page title. <paramref name="titleY"/> is the block offset that
        /// lands the cap height on the template's baseline; it sits above the template's own y
        /// because the display font's line box is taller than its glyphs.
        /// </summary>
        private static void SectionHead(LayersDescriptor layers, string eyebrow, string title,
            float titleSize, string titleColor, float titleY, string gold)
        {
            var size = FitSize(title, 620f, titleSize, titleSize * 0.62f, EmDisplayBold);
            AtCaps(layers, MarginX, 56.0f, 12.3f).Text(eyebrow).Style(Caps(12.3f, gold));
            At(layers, MarginX, titleY, 620f).Text(title).Style(Display(size, titleColor, bold: true));
        }

        private static void Footer(LayersDescriptor layers, string section, int page, string sectionColor, string gold)
        {
            AtCaps(layers, MarginX, FooterY, 7.9f).Text("HOUSE OF PIANOS").Style(Caps(7.9f, gold));
            AtRight(layers, PageW - MarginX, FooterY)
                .Text($"{section}  ·  {page:00}")
                .Style(Sans(7.9f, sectionColor).LetterSpacing(Track));
        }

        private static void Bullets(ColumnDescriptor col, IEnumerable<string> items, string? icon,
            float iconSize, float textOffset, float pitch, float fontSize, string color, float textNudge, string gold,
            float textWidth = 360f)
        {
            foreach (var item in items)
            {
                var size = FitSize(item, textWidth, fontSize, Math.Max(8f, fontSize * 0.72f), EmItalic);
                col.Item().Height(pitch).Row(r =>
                {
                    r.ConstantItem(textOffset).Element(e =>
                    {
                        if (icon != null) e.Width(iconSize).Image(icon);
                        else e.PaddingTop(fontSize * 0.4f).Width(4).Height(4).Background(gold);
                    });
                    r.RelativeItem().PaddingTop(textNudge).Text(item).Style(Serif(size, color));
                });
            }
        }

        private static float ListPitch(int count, float availableHeight, float preferred)
        {
            if (count <= 0) return preferred;
            var needed = preferred * count;
            if (needed <= availableHeight) return preferred;
            return Math.Max(14f, availableHeight / count);
        }

        /// <summary>Product cut-out, unframed, subject centered in the frame above its title.</summary>
        private static void Hero(IContainer container, ProposalPdfModel model, string background,
            string gold, string cream, string charcoal, string paper)
        {
            if (!string.IsNullOrWhiteSpace(model.ProductImagePath) && File.Exists(model.ProductImagePath))
            {
                var trimmed = TrimTransparent(model.ProductImagePath!);
                container.AlignCenter().AlignMiddle().Element(inner =>
                {
                    if (trimmed != null)
                        inner.Image(trimmed).FitArea();
                    else
                        inner.Image(model.ProductImagePath!).FitArea();
                });
                return;
            }

            var onDark = !string.Equals(background, paper, StringComparison.OrdinalIgnoreCase);
            container.AlignCenter().AlignMiddle().Column(inner =>
            {
                inner.Item().AlignCenter().Text(model.BrandName).Style(Caps(10f, gold));
                inner.Item().PaddingTop(8).AlignCenter().Text(model.Model)
                    .Style(Display(18f, onDark ? cream : charcoal));
            });
        }

        /// <summary>
        /// Studio plate: true white behind the photo so cut-outs and JPEGs both read as
        /// a photograph, not as a cream card or a black-alpha hole.
        /// </summary>
        private static void GalleryCard(IContainer container, string? imagePath, ProposalPdfModel model,
            string cardLine, float fillWidth = 0, float fillHeight = 0)
        {
            const string White = "#FFFFFF";
            var fill = fillWidth > 0 && fillHeight > 0;
            var pad = fill ? 2.2f : 5.4f;
            var frame = container.Background(White).Border(0.6f).BorderColor(cardLine)
                .Padding(pad).Background(White);

            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                frame.AlignCenter().AlignMiddle().Text(model.Model).Style(Display(14f, cardLine));
                return;
            }

            var bytes = fill
                ? CoverToBox(imagePath, fillWidth - pad * 2, fillHeight - pad * 2)
                : FlattenOnWhite(imagePath);

            if (bytes == null)
            {
                frame.AlignCenter().AlignMiddle().Image(imagePath).FitArea();
                return;
            }

            if (fill)
            {
                frame.Image(bytes).FitArea();
                return;
            }

            frame.AlignCenter().AlignMiddle().Image(bytes).FitArea();
        }

        /// <summary>
        /// Scale the photo up until it covers the plate, then center-crop. Same idea as
        /// CSS object-fit: cover — the box is full, the picture is not squashed.
        /// </summary>
        private static byte[]? CoverToBox(string path, float boxW, float boxH)
        {
            try
            {
                using var source = SKBitmap.Decode(path);
                if (source == null || source.Width < 1 || source.Height < 1 || boxW <= 0 || boxH <= 0)
                    return null;

                var target = boxW / boxH;
                var sourceAspect = source.Width / (float)source.Height;
                int cropW, cropH, cropX, cropY;
                if (sourceAspect > target)
                {
                    cropH = source.Height;
                    cropW = Math.Max(1, (int)Math.Round(cropH * target));
                    cropX = Math.Max(0, (source.Width - cropW) / 2);
                    cropY = 0;
                }
                else
                {
                    cropW = source.Width;
                    cropH = Math.Max(1, (int)Math.Round(cropW / target));
                    cropX = 0;
                    cropY = Math.Max(0, (source.Height - cropH) / 2);
                }

                cropW = Math.Min(cropW, source.Width - cropX);
                cropH = Math.Min(cropH, source.Height - cropY);

                var info = new SKImageInfo(cropW, cropH, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var plate = new SKBitmap(info);
                plate.Erase(SKColors.White);
                using (var canvas = new SKCanvas(plate))
                {
                    canvas.DrawBitmap(
                        source,
                        new SKRect(cropX, cropY, cropX + cropW, cropY + cropH),
                        new SKRect(0, 0, cropW, cropH));
                }

                using var image = SKImage.FromBitmap(plate);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
                return encoded.ToArray();
            }
            catch
            {
                return null;
            }
        }

        private static byte[]? FlattenOnWhite(string path)
        {
            try
            {
                using var source = SKBitmap.Decode(path);
                if (source == null) return null;

                var info = new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var plate = new SKBitmap(info);
                plate.Erase(SKColors.White);
                using (var canvas = new SKCanvas(plate))
                    canvas.DrawBitmap(source, 0, 0);

                using var image = SKImage.FromBitmap(plate);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
                return encoded.ToArray();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Crops empty transparent padding so the instrument sits in the middle of the hero
        /// frame, level with the title beneath it.
        /// </summary>
        private static byte[]? TrimTransparent(string path)
        {
            try
            {
                using var source = SKBitmap.Decode(path);
                if (source == null) return null;

                if (!TryOpaqueBounds(source, out var bounds))
                    return null;

                var padX = Math.Max(4, bounds.Width / 40);
                var padY = Math.Max(4, bounds.Height / 40);
                var left = Math.Max(0, bounds.Left - padX);
                var top = Math.Max(0, bounds.Top - padY);
                var right = Math.Min(source.Width, bounds.Right + padX);
                var bottom = Math.Min(source.Height, bounds.Bottom + padY);
                var crop = new SKRectI(left, top, right, bottom);

                using var trimmed = new SKBitmap(crop.Width, crop.Height, source.ColorType, source.AlphaType);
                if (!source.ExtractSubset(trimmed, crop))
                    return null;

                using var image = SKImage.FromBitmap(trimmed);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
                return encoded.ToArray();
            }
            catch
            {
                return null;
            }
        }

        private static bool TryOpaqueBounds(SKBitmap bitmap, out SKRectI bounds)
        {
            var minX = bitmap.Width;
            var minY = bitmap.Height;
            var maxX = 0;
            var maxY = 0;
            var step = Math.Max(1, Math.Max(bitmap.Width, bitmap.Height) / 400);

            for (var y = 0; y < bitmap.Height; y += step)
            {
                for (var x = 0; x < bitmap.Width; x += step)
                {
                    if (bitmap.GetPixel(x, y).Alpha < 16) continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
            {
                bounds = default;
                return false;
            }

            bounds = new SKRectI(minX, minY, maxX + 1, maxY + 1);
            return true;
        }

        /// <summary>
        /// Three gallery slots from the image store, falling back to the hero so the page
        /// never shows empty frames.
        /// </summary>
        private string?[] GalleryPaths(ProposalPdfModel model)
        {
            if (model.ProductId is Guid productId && _images != null)
            {
                var hero = _images.ResolveFullPath(productId, 1);
                return new string?[]
                {
                    hero,
                    _images.ResolveFullPath(productId, 2) ?? hero,
                    _images.ResolveFullPath(productId, 3) ?? hero
                };
            }

            var primary = model.ProductImagePath;
            if (string.IsNullOrWhiteSpace(primary) || !File.Exists(primary))
                return new string?[] { null, null, null };

            return new string?[] { primary, primary, primary };
        }

        private static string? BrandAsset(string? fileName)
        {
            var safe = Path.GetFileName(fileName ?? "");
            if (string.IsNullOrWhiteSpace(safe)) return null;

            foreach (var root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var path = Path.Combine(root, "wwwroot", "brand", safe);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        private const float EmDisplay = 0.52f;
        private const float EmDisplayBold = 0.58f;
        private const float EmItalic = 0.48f;
        private const float EmCaps = 0.62f + Track;

        /// <summary>
        /// Shrink a single line so it stays on one line. QuestPDF has no auto-fit,
        /// so this estimates width from average em and clamps between min and max.
        /// </summary>
        private static float FitSize(string text, float maxWidth, float maxSize, float minSize, float avgEm)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0) return maxSize;
            var needed = text.Length * avgEm * maxSize;
            if (needed <= maxWidth) return maxSize;
            return Math.Clamp(maxWidth / (text.Length * avgEm), minSize, maxSize);
        }

        private static string? ClipCopy(string? text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length <= maxChars) return text;
            var cut = text.LastIndexOf(' ', maxChars);
            if (cut < maxChars / 2) cut = maxChars;
            return text[..cut].TrimEnd() + "…";
        }

        private static string FormatIssuedDate(DateTimeOffset issued)
        {
            TimeZoneInfo tz;
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById("Arabian Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                try { tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai"); }
                catch (TimeZoneNotFoundException) { tz = TimeZoneInfo.Utc; }
            }

            var local = TimeZoneInfo.ConvertTime(issued, tz);
            return local.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("en-GB"));
        }

        private static DateTimeOffset ResolveIssuedAt(Proposal proposal) =>
            proposal.UpdatedAt > proposal.CreatedAt.AddMinutes(2)
                ? proposal.UpdatedAt
                : proposal.CreatedAt;

        private static string VatCaption(ProposalPdfModel model)
        {
            if (string.Equals(model.VatMode, "none", StringComparison.OrdinalIgnoreCase))
                return "Prices as quoted";

            if (string.Equals(model.VatMode, "itemised", StringComparison.OrdinalIgnoreCase)
                && model.PriceTotal is > 0)
            {
                var vat = ProposalQuote.VatAmount(model.PriceTotal.Value);
                var inc = model.PriceTotal.Value + vat;
                return $"VAT 5% {model.Currency} {ProposalQuote.FormatMoney(vat)}  ·  Inc. VAT {model.Currency} {ProposalQuote.FormatMoney(inc)}";
            }

            return "VAT as applicable";
        }

        private static string[] MergeIncludes(string[] included, IReadOnlyList<ProposalAddonLine> addons)
        {
            var list = new List<string>();
            foreach (var addon in addons)
            {
                if (!list.Any(x => string.Equals(x, addon.Name, StringComparison.OrdinalIgnoreCase)))
                    list.Add(addon.Name);
            }
            foreach (var item in included)
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                if (!list.Any(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase)))
                    list.Add(item);
            }
            return list.ToArray();
        }

        private static IEnumerable<string> Paragraphs(string? blurb)
        {
            if (string.IsNullOrWhiteSpace(blurb)) yield break;
            foreach (var part in blurb.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0) yield return trimmed;
            }
        }

        private static string? Dimension(IEnumerable<KeyValuePair<string, string>> dimensions, string key) =>
            dimensions.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

        private static string CurrencyName(string code) => code.ToUpperInvariant() switch
        {
            "AED" => "UAE Dirham",
            "USD" => "US Dollars",
            "EUR" => "Euro",
            "GBP" => "Pounds Sterling",
            _ => code
        };

        private static void ApplyDeck(PageDescriptor page, string background, string defaultInk)
        {
            page.Size(Deck);
            page.Margin(0);
            page.PageColor(background);
            page.DefaultTextStyle(x => x.FontColor(defaultInk).FontSize(11).FontFamily(FontBody));
        }

        private static string TrimQuotes(string value) =>
            value.Trim().Trim('"', '“', '”', '\'');

        private static string TitleCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_', ' ').ToLowerInvariant());
        }

        private static string CategoryLine(ProposalPdfModel model, string? length)
        {
            var kind = model.Model.Contains("K-", StringComparison.OrdinalIgnoreCase) ? "Upright Piano"
                : model.Model.Contains("S-", StringComparison.OrdinalIgnoreCase) ? "Baby Grand"
                : model.Model.Contains("D-", StringComparison.OrdinalIgnoreCase) ? "Concert Grand"
                : "Grand Piano";
            if (string.IsNullOrWhiteSpace(length)) return kind;

            // The header carries the metric figure only; imperial stays in the table.
            var metric = length!.Split('(')[0].Trim();
            return $"{kind}  ·  {metric}";
        }

        private static readonly JsonSerializerOptions SnapshotJson = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        /// <summary>Persisted shape of a proposal PDF (all content frozen at save time).</summary>
        private sealed class SnapshotPayload
        {
            public string Reference { get; set; } = "";
            public string ClientName { get; set; } = "Client";
            public string AdvisorName { get; set; } = "Advisor";
            public string? AdvisorTitle { get; set; }
            public string BrandName { get; set; } = "Steinway & Sons";
            public string? BrandBlurb { get; set; }
            public string Model { get; set; } = "";
            public string? Tagline { get; set; }
            public string? Blurb { get; set; }
            public string? Finish { get; set; }
            public string Currency { get; set; } = "AED";
            public decimal? PriceTotal { get; set; }
            public int ValidityDays { get; set; } = 14;
            public string VatMode { get; set; } = "line";
            public string[]? Features { get; set; }
            public string[]? Included { get; set; }
            public string[]? Excluded { get; set; }
            public string[]? Finishes { get; set; }
            public Dictionary<string, string>? Dimensions { get; set; }
            public string? AvailabilityNote { get; set; }
            public string BusinessName { get; set; } = "";
            public string? BusinessBlurb { get; set; }
            public string Phone { get; set; } = "";
            public string Website { get; set; } = "";
            public string Instagram { get; set; } = "";
            public string AddressLine { get; set; } = "";
            public string? ProductImagePath { get; set; }
            public DateTimeOffset IssuedAt { get; set; }
            public Guid? ProductId { get; set; }
            public int Qty { get; set; } = 1;
            public List<ProposalAddonLine>? Addons { get; set; }
        }

        public static JsonDocument BuildSnapshot(ProposalPdfModel model)
        {
            var json = JsonSerializer.Serialize(model, SnapshotJson);
            return JsonDocument.Parse(json);
        }

        public static ProposalPdfModel? TryLoadModelFromSnapshot(JsonDocument? snapshot)
        {
            if (snapshot == null) return null;
            try
            {
                var p = JsonSerializer.Deserialize<SnapshotPayload>(
                    snapshot.RootElement.GetRawText(),
                    SnapshotJson);
                if (p == null || string.IsNullOrWhiteSpace(p.Reference) || string.IsNullOrWhiteSpace(p.Model))
                    return null;

                return new ProposalPdfModel(
                    Reference: p.Reference,
                    ClientName: p.ClientName,
                    AdvisorName: p.AdvisorName,
                    AdvisorTitle: p.AdvisorTitle,
                    BrandName: p.BrandName,
                    BrandBlurb: p.BrandBlurb,
                    Model: p.Model,
                    Tagline: p.Tagline,
                    Blurb: p.Blurb,
                    Finish: p.Finish,
                    Currency: string.IsNullOrWhiteSpace(p.Currency) ? "AED" : p.Currency,
                    PriceTotal: p.PriceTotal,
                    ValidityDays: p.ValidityDays <= 0 ? 14 : p.ValidityDays,
                    VatMode: string.IsNullOrWhiteSpace(p.VatMode) ? "line" : p.VatMode,
                    Features: p.Features ?? Array.Empty<string>(),
                    Included: p.Included ?? Array.Empty<string>(),
                    Excluded: p.Excluded ?? Array.Empty<string>(),
                    Finishes: p.Finishes ?? Array.Empty<string>(),
                    Dimensions: CleanDimensions(p.Dimensions),
                    AvailabilityNote: p.AvailabilityNote,
                    BusinessName: p.BusinessName,
                    BusinessBlurb: p.BusinessBlurb,
                    Phone: p.Phone,
                    Website: p.Website,
                    Instagram: p.Instagram,
                    AddressLine: p.AddressLine,
                    ProductImagePath: p.ProductImagePath,
                    IssuedAt: p.IssuedAt == default ? DateTimeOffset.UtcNow : p.IssuedAt,
                    ProductId: p.ProductId,
                    Qty: p.Qty < 1 ? 1 : p.Qty,
                    Addons: p.Addons
                );
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Re-resolve product image from disk using ProductId when the stored path is stale.
        /// Text/pricing content always comes from the snapshot.
        /// </summary>
        private ProposalPdfModel WithResolvedImage(ProposalPdfModel model)
        {
            if (!string.IsNullOrWhiteSpace(model.ProductImagePath) && File.Exists(model.ProductImagePath))
                return model;

            if (model.ProductId is Guid productId)
            {
                var path = _images?.ResolveFullPath(productId, 1)
                    ?? Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "wwwroot", "images", "products",
                        ProductImageStore.FileName(productId, 1));
                if (File.Exists(path))
                    return model with { ProductImagePath = path };
            }

            return model with { ProductImagePath = null };
        }

        /// <summary>
        /// Rebuilds the PDF content model from the latest proposal + catalog data and
        /// persists it on the proposal.snapshot. Call on save / update / send.
        /// </summary>
        public async Task<bool> RefreshContentAsync(AppDbContext db, Guid proposalId)
        {
            var built = await BuildModelAsync(db, proposalId);
            if (built == null) return false;

            var (proposal, model) = built.Value;
            proposal.Snapshot = BuildSnapshot(model);
            proposal.PdfUrl = null; // content snapshot is the source of truth
            proposal.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Renders PDF from the stored snapshot when present (immutable view).
        /// Falls back to building + saving a snapshot only if none exists yet.
        /// </summary>
        public async Task<(byte[] Bytes, string FileName)?> RenderAsync(AppDbContext db, Guid proposalId)
        {
            var proposal = await db.Proposals.FirstOrDefaultAsync(p => p.Id == proposalId);
            if (proposal == null) return null;

            var model = TryLoadModelFromSnapshot(proposal.Snapshot);

            if (model == null)
            {
                // Legacy / never saved with content — build once and freeze
                var built = await BuildModelAsync(db, proposalId);
                if (built == null) return null;
                model = built.Value.model;
                proposal.Snapshot = BuildSnapshot(model);
                proposal.PdfUrl = null;
                proposal.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }

            model = WithResolvedImage(model);
            var template = await db.Templates.FirstOrDefaultAsync(t => t.Id == proposal.TemplateId);
            var palette = PdfPalette.FromSchema(template?.PageSchema);
            var logoFile = BrandStyleService.LogoFromSchema(template?.PageSchema);
            var rendererKey =
                BrandStyleService.RendererFromSchema(template?.PageSchema) ?? template?.Key;
            var bytes = GenerateFor(rendererKey, model, palette, logoFile);
            var fileName =
                $"{proposal.Reference}_{SanitizeFilePart(model.ClientName)}_{SanitizeFilePart(model.Model)}.pdf";
            return (bytes, fileName);
        }

        public async Task<(Proposal proposal, ProposalPdfModel model)?> BuildModelAsync(AppDbContext db, Guid id)
        {
            var proposal = await db.Proposals.FirstOrDefaultAsync(p => p.Id == id);
            if (proposal == null) return null;

            var item = await db.ProposalItems.FirstOrDefaultAsync(i => i.ProposalId == id);
            if (item == null) return null;

            var product = await db.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId);
            if (product == null) return null;

            var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == product.BrandId);
            var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == proposal.ClientId);
            var advisor = await db.Users.FirstOrDefaultAsync(u => u.Id == proposal.AdvisorId);
            var business = await db.Businesses.FirstOrDefaultAsync(b => b.Id == proposal.BusinessId);

            var addons = ProposalQuote.ParseAddons(item.Addons);
            var finalPrice = ProposalQuote.Total(item.PriceOverride ?? item.UnitPrice, item.Qty, item.Addons);

            var dimensions = ReadDimensions(product.Dimensions);

            var imagePath = _images?.ResolveFullPath(product.Id, 1)
                ?? Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot", "images", "products",
                    ProductImageStore.FileName(product.Id, 1));
            if (!File.Exists(imagePath))
                imagePath = null;

            var advisorTitle = advisor?.Role?.ToLowerInvariant() switch
            {
                "admin" => "Director",
                "manager" => "Sales Manager",
                _ => "Sales Advisor"
            };

            var model = new ProposalPdfModel(
                Reference: proposal.Reference,
                ClientName: client?.Name ?? "Client",
                AdvisorName: advisor?.Name ?? "Advisor",
                AdvisorTitle: advisorTitle,
                BrandName: brand?.Name ?? "Steinway & Sons",
                BrandBlurb: brand?.Blurb,
                Model: product.Model,
                Tagline: ClipCopy(product.Tagline, ProposalCopyLimits.TaglineMaxChars),
                Blurb: ClipCopy(product.Blurb, ProposalCopyLimits.BlurbMaxChars),
                Finish: item.Finish,
                Currency: proposal.Currency,
                PriceTotal: finalPrice ?? proposal.PriceTotal,
                ValidityDays: proposal.ValidityDays,
                VatMode: proposal.VatMode,
                Features: product.Features ?? Array.Empty<string>(),
                Included: item.Included?.Length > 0 ? item.Included : product.DefaultIncludes ?? Array.Empty<string>(),
                Excluded: item.Excluded?.Length > 0 ? item.Excluded : product.DefaultExcludes ?? Array.Empty<string>(),
                Finishes: product.Finishes ?? Array.Empty<string>(),
                Dimensions: dimensions,
                AvailabilityNote: product.AvailabilityNote,
                BusinessName: business?.Name ?? "",
                BusinessBlurb: business?.Blurb,
                Phone: business?.Phone ?? "",
                Website: business?.Website ?? "",
                Instagram: business?.Instagram ?? "",
                AddressLine: business?.Address ?? "",
                ProductImagePath: !string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath)
                    ? imagePath
                    : null,
                IssuedAt: ResolveIssuedAt(proposal),
                ProductId: product.Id,
                Qty: item.Qty < 1 ? 1 : item.Qty,
                Addons: addons
            );

            return (proposal, model);
        }

        /// <summary>
        /// Postgres jsonb sorts object keys by length, so the stored order is not the reading
        /// order. Impose the sequence the specifications table expects and let anything
        /// unrecognised follow in its original position.
        /// </summary>
        private static List<KeyValuePair<string, string>> OrderDimensions(
            IReadOnlyDictionary<string, string> dimensions)
        {
            var order = new[] { "length", "height", "width", "depth", "weight", "setting" };

            return dimensions
                .OrderBy(entry =>
                {
                    var index = Array.IndexOf(order, entry.Key.ToLowerInvariant());
                    return index < 0 ? order.Length : index;
                })
                .ToList();
        }

        /// <summary>
        /// Snapshots frozen before dimensions were sanitised can still carry the "RootElement"
        /// wrapper, so drop it here too and restore the case-insensitive lookup.
        /// </summary>
        private static Dictionary<string, string> CleanDimensions(Dictionary<string, string>? stored)
        {
            var dimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (stored == null) return dimensions;

            foreach (var (label, value) in stored)
            {
                if (string.Equals(label, "RootElement", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrWhiteSpace(value)) continue;
                dimensions[label] = value.Trim();
            }

            return dimensions;
        }

        /// <summary>
        /// Reads the product's jsonb dimensions into label/value pairs, skipping anything the
        /// specifications table cannot render: non-object roots, nested values, and the
        /// "RootElement" wrapper left behind by rows saved through a mis-serialised client.
        /// </summary>
        private static Dictionary<string, string> ReadDimensions(JsonDocument? source)
        {
            var dimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (source == null || source.RootElement.ValueKind != JsonValueKind.Object)
                return dimensions;

            foreach (var prop in source.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "RootElement", StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => prop.Value.ToString(),
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(value))
                    dimensions[prop.Name] = value!.Trim();
            }

            return dimensions;
        }

        private static string SanitizeFilePart(string value)
        {
            var cleaned = string.Join("-", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrWhiteSpace(cleaned) ? "X" : cleaned.Replace(' ', '-');
        }
    }
}
