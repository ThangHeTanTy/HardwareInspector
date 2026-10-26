using System.Net;
using System.Text;
using HardwareInspector.Core.Abstractions;
using HardwareInspector.Core.Models;
using HardwareInspector.Core.Models.Assessment;
using HardwareInspector.Core.Utils;
using static HardwareInspector.Core.Localization.Loc;

namespace HardwareInspector.Reporting.Exporters;

/// <summary>
/// Báo cáo HTML một file, không phụ thuộc mạng — đưa được cho người bán xem tại chỗ
/// hoặc in ra làm biên bản bàn giao.
/// </summary>
public sealed class HtmlReportExporter : IReportExporter
{
    public string Format => "HTML";
    public string FileExtension => ".html";

    public async Task<string> ExportAsync(MachineAssessment assessment, SystemSnapshot snapshot,
        string outputPath, CancellationToken ct = default)
    {
        var html = Build(assessment, snapshot);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, html, new UTF8Encoding(true), ct);
        return outputPath;
    }

    private static string Build(MachineAssessment a, SystemSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.Append(HtmlTemplate.Head(a.MachineTitle));

        sb.Append($"""
            <header>
              <div class="brand">
                {HtmlTemplate.LogoSvg}
              </div>
              <p class="eyebrow">{S("Biên bản kiểm tra phần cứng", "Hardware inspection report")}</p>
              <h1>{E(a.MachineTitle)}</h1>
              <p class="meta">{S("Lập lúc", "Generated")} {a.GeneratedAtLocal:HH:mm dd/MM/yyyy} · {E(snapshot.OperatingSystem.Caption)}
              {(snapshot.Firmware.RanWithAdminRights
                  ? S("· chạy với quyền Administrator", "· run with Administrator rights")
                  : S("· <strong>chưa chạy quyền Administrator</strong>", "· <strong>not run as Administrator</strong>"))}</p>
            </header>

            <section class="verdict">
              <div class="score" style="--c:{a.OverallRating.ToHexColor()}">
                <span class="num">{a.OverallScore}</span><span class="den">/100</span>
                <span class="label">{E(a.OverallRating.ToText())}</span>
              </div>
              <div class="verdict-text">
                <p>{E(a.Summary)}</p>
                <p class="trust"><strong>{S("Mức tin cậy khi mua lại:", "Second-hand trust level:")}</strong> {E(a.Trust.Level.ToText())} ({a.Trust.TrustScore}/100)</p>
                <p>{E(a.Trust.Verdict)}</p>
              </div>
            </section>
            """);

        AppendAdvice(sb, a.Advice);

        // Bảng tổng hợp linh kiện
        sb.Append(S("<section><h2>Tổng quan từng linh kiện</h2><table class=\"summary\"><thead><tr>" +
                    "<th>Linh kiện</th><th>Tên</th><th>Đánh giá</th><th>Điểm</th><th>Thời gian hoạt động</th><th>Sức khoẻ</th>" +
                    "</tr></thead><tbody>",
                    "<section><h2>Component overview</h2><table class=\"summary\"><thead><tr>" +
                    "<th>Component</th><th>Name</th><th>Rating</th><th>Score</th><th>Operating time</th><th>Health</th>" +
                    "</tr></thead><tbody>"));

        foreach (var c in a.Components)
        {
            sb.Append($"""
                <tr>
                  <td>{E(Translate(c.Kind))}</td>
                  <td>{E(c.DisplayName)}</td>
                  <td><span class="pill" style="--c:{c.Rating.ToHexColor()}">{E(c.Rating.ToText())}</span></td>
                  <td class="num-cell">{(c.Rating == HealthRating.Unknown ? "—" : c.Score.ToString())}</td>
                  <td>{(c.OperatingTime.HasValue ? E(c.OperatingTime.Value.ToHumanDuration()) : "—")}</td>
                  <td>{(c.HealthPercent.HasValue ? $"{c.HealthPercent:0.#}%" : "—")}</td>
                </tr>
                """);
        }
        sb.Append("</tbody></table></section>");

        // Cảnh báo firmware
        sb.Append($"""
            <section class="firmware">
              <h2>{S("BIOS và chuỗi khởi động", "BIOS and boot chain")}</h2>
              <p class="status" style="--c:{StatusColor(snapshot.Firmware.Status)}">
                {E(snapshot.Firmware.Status.ToText())}
              </p>
            </section>
            """);

        // Chi tiết từng linh kiện
        foreach (var c in a.Components)
        {
            sb.Append($"<section class=\"component\"><h2>{E(Translate(c.Kind))} — {E(c.DisplayName)}</h2>");
            sb.Append($"<p class=\"headline\">{E(c.Headline)}</p>");

            if (c.Metrics.Count > 0)
            {
                sb.Append("<table class=\"metrics\"><tbody>");
                foreach (var m in c.Metrics)
                    sb.Append($"<tr><th>{E(m.Label)}</th><td>{E(m.Value)}" +
                              (string.IsNullOrWhiteSpace(m.Note) ? "" : $"<span class=\"note\">{E(m.Note!)}</span>") +
                              "</td></tr>");
                sb.Append("</tbody></table>");
            }

            var findings = c.Findings.Where(f => f.Severity >= Severity.Notice).ToList();
            if (findings.Count > 0)
            {
                sb.Append("<ul class=\"findings\">");
                foreach (var f in findings.OrderByDescending(f => f.Severity))
                {
                    sb.Append($"<li class=\"sev-{f.Severity.ToString().ToLowerInvariant()}\">" +
                              $"<strong>{E(f.Title)}</strong>");
                    if (!string.IsNullOrWhiteSpace(f.Detail)) sb.Append($"<p>{E(f.Detail!)}</p>");
                    if (!string.IsNullOrWhiteSpace(f.Recommendation))
                        sb.Append($"<p class=\"rec\">{S("Nên làm:", "What to do:")} {E(f.Recommendation!)}</p>");
                    sb.Append("</li>");
                }
                sb.Append("</ul>");
            }

            sb.Append("</section>");
        }

        // Đối chiếu chéo
        sb.Append(S("<section><h2>Đối chiếu chéo giữa các nguồn dữ liệu</h2><ul class=\"crosschecks\">",
                    "<section><h2>Cross-checks between data sources</h2><ul class=\"crosschecks\">"));
        foreach (var cc in a.Trust.CrossChecks)
            sb.Append($"<li class=\"{(cc.Passed ? "pass" : "fail")}\"><strong>{E(cc.Name)}</strong>" +
                      $"<p>{E(cc.Explanation)}</p></li>");
        sb.Append("</ul></section>");

        // Tra bảo hành
        if (a.WarrantyLookups.Count > 0)
        {
            sb.Append(S("<section><h2>Đầu mối tra bảo hành</h2><table class=\"summary\"><thead><tr>" +
                        "<th>Linh kiện</th><th>Hãng</th><th>Serial</th><th>Ghi chú</th>" +
                        "</tr></thead><tbody>",
                        "<section><h2>Warranty lookups</h2><table class=\"summary\"><thead><tr>" +
                        "<th>Component</th><th>Vendor</th><th>Serial</th><th>Note</th>" +
                        "</tr></thead><tbody>"));

            foreach (var w in a.WarrantyLookups)
            {
                var serialCell = w.HasSerial
                    ? $"<code>{E(w.Serial)}</code>"
                    : S("<span class=\"muted\">không đọc được</span>", "<span class=\"muted\">not readable</span>");
                var vendorCell = string.IsNullOrWhiteSpace(w.Url)
                    ? E(w.Vendor)
                    : $"<a href=\"{E(w.Url)}\">{E(w.Vendor)}</a>";

                sb.Append($"<tr><td>{E(w.ComponentName)}</td><td>{vendorCell}</td>" +
                          $"<td>{serialCell}</td><td class=\"note-cell\">{E(w.Note)}</td></tr>");
            }

            sb.Append("</tbody></table></section>");
        }

        // Checklist thủ công
        sb.Append(S("<section><h2>Việc cần kiểm tra bằng tay</h2>" +
                    "<p class=\"intro\">Những mục dưới đây phần mềm không thay thế được. " +
                    "Hãy làm đủ trước khi trả tiền. Công cụ gợi ý đều dẫn tới trang chính thức của nhà phát triển — " +
                    "đừng tải từ trang trung gian.</p><ol class=\"checklist\">",
                    "<section><h2>Manual checks</h2>" +
                    "<p class=\"intro\">Software cannot replace the items below. " +
                    "Do all of them before paying. Suggested tools link to the developers' official pages — " +
                    "do not download from third-party sites.</p><ol class=\"checklist\">"));
        foreach (var item in a.Trust.ManualChecklist)
        {
            sb.Append($"<li>{E(item.Text)}");
            if (item.HasTools)
            {
                sb.Append("<ul class=\"tools\">");
                foreach (var tool in item.Tools)
                    sb.Append($"<li><a href=\"{E(tool.Url)}\">{E(tool.Name)}</a> " +
                              $"<span class=\"cost\">({E(tool.Cost)})</span> — {E(tool.Purpose)}</li>");
                sb.Append("</ul>");
            }
            sb.Append("</li>");
        }
        sb.Append("</ol></section>");

        sb.Append(HtmlTemplate.Foot());
        return sb.ToString();
    }

    /// <summary>Kết luận cho người mua: nên mua hay không, việc người bán phải làm, chi phí quy ra tiền.</summary>
    private static void AppendAdvice(StringBuilder sb, PurchaseAdvice advice)
    {
        if (advice.Decision == PurchaseDecision.Unknown) return;

        sb.Append($"<section class=\"advice\"><h2 style=\"color:{advice.ColorHex}\">{E(advice.Headline)}</h2>" +
                  $"<p>{E(advice.Explanation)}</p>");

        void List(string title, IEnumerable<string> items, string cls)
        {
            var list = items.ToList();
            if (list.Count == 0) return;
            sb.Append($"<h3>{E(title)}</h3><ul class=\"findings\">");
            foreach (var item in list) sb.Append($"<li class=\"{cls}\">{E(item)}</li>");
            sb.Append("</ul>");
        }

        List(S("Vì sao không nên mua", "Why not to buy"), advice.Blockers, "sev-critical");
        List(S("Người bán phải làm xong trước khi bạn trả tiền", "The seller must do these before you pay"), advice.Conditions, "sev-warning");

        void Costs(string title, CostKind kind)
        {
            var rows = advice.Costs.Where(c => c.Kind == kind).ToList();
            if (rows.Count == 0) return;
            sb.Append($"<h3>{E(title)}</h3><table class=\"summary\"><tbody>");
            foreach (var c in rows)
                sb.Append($"<tr><td><strong>{E(c.Title)}</strong><br><span class=\"muted\">{E(c.Reason)}</span></td>" +
                          $"<td class=\"num-cell\">{E(FormatHelpers.VndRange(c.MinVnd, c.MaxVnd))}</td></tr>");
            sb.Append("</tbody></table>");
        }

        Costs(S("Chi phí khắc phục ước tính", "Estimated repair costs"), CostKind.Repair);
        if (advice.HasRepairs)
            sb.Append($"<p><strong>{E(S("Tổng chi phí khắc phục: ", "Total repair cost: "))}" +
                      $"{E(FormatHelpers.VndRange(advice.RepairMinVnd, advice.RepairMaxVnd))}</strong></p>");
        Costs(S("Nâng cấp nên cân nhắc (không bắt buộc)", "Upgrades worth considering (optional)"), CostKind.Upgrade);

        if (!string.IsNullOrWhiteSpace(advice.NegotiationTip))
            sb.Append($"<p class=\"rec\">{E(advice.NegotiationTip)}</p>");
        sb.Append($"<p class=\"muted\">{E(S("Giá là mức tham khảo thị trường Việt Nam cho linh kiện phổ thông kèm công thay.", "Prices are Vietnamese market references for common parts including labour."))}</p>");
        sb.Append("</section>");
    }

    private static string StatusColor(IntegrityStatus status) => status switch
    {
        IntegrityStatus.Clean => "#2FBF71",
        IntegrityStatus.Unverified => "#8A8F98",
        IntegrityStatus.Suspicious => "#F4A259",
        IntegrityStatus.Compromised => "#E5544B",
        _ => "#8A8F98"
    };

    private static string Translate(ComponentKind kind) => kind.ToText();

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
