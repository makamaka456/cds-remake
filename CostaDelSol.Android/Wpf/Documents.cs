// WpfLite — 흐르는 문서(FlowDocument)의 작은 대용품. PC 판 「릴리즈 노트」 창(ReleaseNotesDialog)이 쓴다.
// 문단(Paragraph)마다 줄 바꿈하는 글 줄(TextBlock) 하나로 펴서 스크롤 칸(ScrollViewer)에 쌓는다.
// 문단 안의 토막별 색 · 굵기(찾은 낱말 칠하기)는 문단 하나로 합쳐 그린다 — 글은 그대로 다 보인다.

using System.Windows.Media;

namespace System.Windows.Documents
{
    /// <summary>문서의 덩이(문단 등).</summary>
    public abstract class Block : TextElement
    {
        public Thickness Margin { get; set; }
    }

    public sealed class Paragraph : Block
    {
        public Paragraph() { }
        public Paragraph(Inline inline) { Inlines.Add(inline); }
        public List<Inline> Inlines { get; } = [];
        public double TextIndent { get; set; }
        internal string Plain => string.Concat(Inlines.Select(i => i.Plain));
    }

    /// <summary>누르면 주소를 여는 글 토막.</summary>
    public sealed class Hyperlink : Inline
    {
        public Hyperlink() { }
        public Hyperlink(Inline inner) { Inner = inner; }
        private Inline? Inner { get; }
        public Uri? NavigateUri { get; set; }
#pragma warning disable CS0067
        public event Navigation.RequestNavigateEventHandler? RequestNavigate;
#pragma warning restore CS0067
        internal override string Plain => Inner?.Plain ?? "";
    }

    public sealed class FlowDocument
    {
        public double FontSize { get; set; } = 12;
        public FontFamily? FontFamily { get; set; }
        public Thickness PagePadding { get; set; }
        public Brush? Foreground { get; set; }
        public Brush? Background { get; set; }
        public List<Block> Blocks { get; } = [];
    }
}

namespace System.Windows.Navigation
{
    public sealed class RequestNavigateEventArgs : RoutedEventArgs
    {
        public RequestNavigateEventArgs(Uri uri, string target) { Uri = uri; Target = target; }
        public Uri Uri { get; }
        public string Target { get; }
    }

    public delegate void RequestNavigateEventHandler(object sender, RequestNavigateEventArgs e);
}

namespace System.Windows.Controls
{
    using System.Windows.Documents;

    /// <summary>흐르는 문서를 위아래로 굴려 보는 칸.</summary>
    public class FlowDocumentScrollViewer : ScrollViewer
    {
        private FlowDocument? _document;

        public bool IsToolBarVisible { get; set; }

        public FlowDocument? Document
        {
            get => _document;
            set
            {
                if (ReferenceEquals(_document, value)) return;
                _document = value;
                Content = value == null ? null : Lay(value);
                ScrollToTop();
            }
        }

        private static FrameworkElement Lay(FlowDocument doc)
        {
            var stack = new StackPanel { Margin = doc.PagePadding };
            foreach (var block in doc.Blocks)
            {
                if (block is not Paragraph p) continue;
                bool bold = p.FontWeight is { } w && w.Weight >= 600;
                stack.Children.Add(new TextBlock
                {
                    Text = p.Plain,
                    FontSize = p.FontSize ?? doc.FontSize,
                    FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = p.Foreground ?? doc.Foreground ?? Brushes.Black,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = p.Margin,
                });
            }
            return stack;
        }
    }
}
