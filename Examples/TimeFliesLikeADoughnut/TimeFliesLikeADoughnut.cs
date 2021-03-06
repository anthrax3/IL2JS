using System;
using System.Windows.Browser;
using Microsoft.LiveLabs.Html;
using Microsoft.LiveLabs.JavaScript.IL2JS;
using System.Concurrency;
using System.Disposables;
using System.Linq;

namespace TimeFliesLikeADoughnut
{
    public class HtmlObservable : IObservable<HtmlEvent>
    {
        readonly public Action<HtmlEventHandler> addHandler;
        readonly public Action<HtmlEventHandler> removeHandler;

        public HtmlObservable(Action<HtmlEventHandler> addHandler, Action<HtmlEventHandler> removeHandler)
        {
            this.addHandler = addHandler;
            this.removeHandler = removeHandler;
        }

        public IDisposable Subscribe(IObserver<HtmlEvent> observer)
        {
            addHandler(observer.OnNext);
            return Disposable.Create(() => removeHandler(observer.OnNext));
        }
    }

    public partial class TimeFliesLikeADoughnutPage : Page
    {
        [EntryPoint]
        public static void Run()
        {
            new TimeFliesLikeADoughnutPage();
        }


        public TimeFliesLikeADoughnutPage()
        {
            InitializeComponent();

            var document = Window.Document;
            var mouseMove = new HtmlObservable(h => document.MouseMove += h, h => document.MouseMove -= h);

            var text = "time flies like a doughnut";
            var container = new Div { Style = { Width = "100%", Height = "100%", FontFamily = "Consolas, monospace", Overflow = "hidden" } };
            document.Body.AppendChild(container);
            for (var i = 0; i < text.Length; i++)
            {
                var j = i;
                var s = new Span { InnerText = text[j].ToString(), Style = { Position = "absolute" } };
                container.AppendChild(s);
                mouseMove.Delay(TimeSpan.FromMilliseconds((double)j * 100.0)).Subscribe
                    (mouseEvent =>
                         {
                             s.Style.Top = mouseEvent.ClientY + "px";
                             s.Style.Left = mouseEvent.ClientX + j * 10 + 15 + "px";
                         });
            }
        }
    }
}
