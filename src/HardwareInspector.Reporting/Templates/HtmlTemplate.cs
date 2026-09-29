namespace HardwareInspector.Reporting.Exporters;

/// <summary>Khung HTML và CSS cho báo cáo. Tách riêng để dễ chỉnh giao diện.</summary>
internal static class HtmlTemplate
{
    /// <summary>
    /// Logo nhúng thẳng dạng SVG thay vì tham chiếu file ảnh.
    /// Biên bản phải mở được khi gửi qua email hay chép sang máy khác,
    /// nên nó không được phụ thuộc vào bất kỳ tệp nào bên ngoài.
    /// </summary>
    public const string LogoSvg =
        """
        <svg viewBox="0 0 512 512" width="56" height="56" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
          <circle cx="76" cy="248" r="50" fill="#F4A259"/>
          <circle cx="58" cy="308" r="58" fill="#F4A259"/>
          <circle cx="80" cy="366" r="50" fill="#F4A259"/>
          <circle cx="436" cy="248" r="50" fill="#F4A259"/>
          <circle cx="454" cy="308" r="58" fill="#F4A259"/>
          <circle cx="432" cy="366" r="50" fill="#F4A259"/>
          <circle cx="140" cy="182" r="44" fill="#F4A259"/>
          <polygon points="242,164 348,192 322,64" fill="#E5544B"/>
          <circle cx="322" cy="56" r="26" fill="#2FBF71"/>
          <circle cx="256" cy="308" r="168" fill="#FFDCC0"/>
          <circle cx="352" cy="286" r="18" fill="#232833"/>
          <path d="M204 408 Q278 470 352 408" fill="none" stroke="#232833" stroke-width="22" stroke-linecap="round"/>
          <circle cx="278" cy="358" r="42" fill="#E5544B"/>
          <path d="M170 364 Q134 428 152 474" fill="none" stroke="#3A6FD8" stroke-width="11" stroke-linecap="round"/>
          <circle cx="152" cy="476" r="17" fill="#3A6FD8"/>
          <circle cx="188" cy="280" r="88" fill="#4C8DFF" fill-opacity="0.22"/>
          <circle cx="188" cy="280" r="88" fill="none" stroke="#4C8DFF" stroke-width="24"/>
          <circle cx="188" cy="280" r="34" fill="#232833"/>
        </svg>
        """;

    public static string Head(string title) => $$"""
        <!DOCTYPE html>
        <html lang="vi">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Biên bản kiểm tra — {{System.Net.WebUtility.HtmlEncode(title)}}</title>
        <style>
          :root {
            --bg: #0F1115; --panel: #171A21; --line: #262A33;
            --text: #E6E8EC; --dim: #9AA0AA; --accent: #4C8DFF;
          }
          * { box-sizing: border-box; }
          body {
            margin: 0; padding: 32px 24px 64px;
            background: var(--bg); color: var(--text);
            font-family: "Segoe UI", system-ui, -apple-system, sans-serif;
            font-size: 15px; line-height: 1.6;
            max-width: 1040px; margin-inline: auto;
          }
          header { border-bottom: 1px solid var(--line); padding-bottom: 20px; margin-bottom: 28px; }
          .brand { margin-bottom: 10px; }
          .brand svg { display: block; }
          .eyebrow { color: var(--dim); text-transform: uppercase; letter-spacing: .12em;
                     font-size: 11px; margin: 0 0 6px; }
          h1 { font-size: 28px; margin: 0 0 8px; font-weight: 600; }
          h2 { font-size: 18px; margin: 0 0 14px; font-weight: 600; }
          .meta { color: var(--dim); font-size: 13px; margin: 0; }
          section { background: var(--panel); border: 1px solid var(--line);
                    border-radius: 10px; padding: 22px; margin-bottom: 18px; }
          .verdict { display: flex; gap: 28px; align-items: flex-start; flex-wrap: wrap; }
          .score { flex: 0 0 auto; text-align: center; padding: 18px 26px;
                   border: 2px solid var(--c); border-radius: 12px; min-width: 150px; }
          .score .num { font-size: 46px; font-weight: 700; color: var(--c); }
          .score .den { font-size: 18px; color: var(--dim); }
          .score .label { display: block; margin-top: 6px; color: var(--c); font-weight: 600; }
          .verdict-text { flex: 1 1 380px; }
          .verdict-text p { margin: 0 0 10px; }
          .trust { color: var(--text); }
          table { width: 100%; border-collapse: collapse; font-size: 14px; }
          th, td { text-align: left; padding: 9px 10px; border-bottom: 1px solid var(--line);
                   vertical-align: top; }
          thead th { color: var(--dim); font-weight: 500; font-size: 12px;
                     text-transform: uppercase; letter-spacing: .06em; }
          .metrics th { width: 34%; color: var(--dim); font-weight: 500; }
          .num-cell { font-variant-numeric: tabular-nums; }
          .note { display: block; color: var(--dim); font-size: 12.5px; margin-top: 2px; }
          .pill { display: inline-block; padding: 2px 10px; border-radius: 999px;
                  background: color-mix(in srgb, var(--c) 18%, transparent);
                  color: var(--c); font-size: 12.5px; font-weight: 600; }
          .status { font-size: 17px; font-weight: 600; color: var(--c); margin: 0; }
          .headline { color: var(--dim); margin: 0 0 14px; }
          ul.findings { list-style: none; padding: 0; margin: 16px 0 0; }
          ul.findings li { border-left: 3px solid var(--line); padding: 8px 0 8px 14px; margin-bottom: 12px; }
          ul.findings li p { margin: 4px 0 0; color: var(--dim); font-size: 14px; }
          ul.findings li p.rec { color: var(--accent); }
          .sev-critical { border-left-color: #E5544B !important; }
          .sev-warning  { border-left-color: #F4A259 !important; }
          .sev-notice   { border-left-color: #8AC926 !important; }
          ul.crosschecks { list-style: none; padding: 0; margin: 0; }
          ul.crosschecks li { padding: 10px 0 10px 26px; position: relative;
                              border-bottom: 1px solid var(--line); }
          ul.crosschecks li::before { position: absolute; left: 0; top: 10px; font-weight: 700; }
          ul.crosschecks li.pass::before { content: "✓"; color: #2FBF71; }
          ul.crosschecks li.fail::before { content: "!"; color: #F4A259; }
          ul.crosschecks li p { margin: 3px 0 0; color: var(--dim); font-size: 14px; }
          ol.checklist { padding-left: 20px; margin: 0; }
          ol.checklist li { margin-bottom: 8px; }
          ul.tools { list-style: none; padding: 0; margin: 5px 0 4px; }
          ul.tools li { margin: 0 0 4px; font-size: 13px; color: var(--dim); }
          ul.tools li a { font-weight: 600; }
          ul.tools .cost { font-size: 11.5px; }
          .intro { color: var(--dim); margin: 0 0 14px; }
          code { font-family: Consolas, monospace; font-size: 12.5px;
                 background: rgba(255,255,255,.06); padding: 1px 5px; border-radius: 3px; }
          a { color: var(--accent); }
          .muted { color: var(--dim); }
          .note-cell { color: var(--dim); font-size: 12.5px; max-width: 380px; }
          footer { color: var(--dim); font-size: 12.5px; text-align: center;
                   padding-top: 22px; border-top: 1px solid var(--line); }
          @media print {
            body { background: #fff; color: #111; }
            section { background: #fff; border-color: #ddd; break-inside: avoid; }
            :root { --text: #111; --dim: #555; --line: #ddd; }
          }
        </style>
        </head>
        <body>
        """;

    public static string Foot() => """
        <footer>
          Báo cáo do Hardware Inspector tạo tự động. Các phát hiện được đánh dấu là suy đoán
          cần đối chiếu thêm bằng kiểm tra thủ công trước khi kết luận.
        </footer>
        </body>
        </html>
        """;
}
