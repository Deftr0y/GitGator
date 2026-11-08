using System;
using System.Reflection;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class About : ChromelessWindow
    {
        public About()
        {
            CloseOnESC = true;
            InitializeComponent();

            var assembly = Assembly.GetExecutingAssembly();
            var meta = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
            foreach (var attr in meta)
            {
                if (attr.Key.Equals("BuildDate", StringComparison.OrdinalIgnoreCase) && DateTime.TryParse(attr.Value, out var date))
                    TxtReleaseDate.Text = App.Text("About.ReleaseDate", Models.DateTimeFormat.Format(date, true));
            }

            var version = assembly.GetName().Version;
            TxtVersion.Text = $"v{version?.ToString(3)}-dev";

            var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>();
            if (copyright != null)
                TxtCopyright.Text = copyright.Copyright;
        }

        private void OnVisitReleaseNotes(object _, RoutedEventArgs e)
        {
            Native.OS.OpenBrowser("https://github.com/Deftr0y/GitGator/releases");
            e.Handled = true;
        }

        private void OnVisitWebsite(object _, RoutedEventArgs e)
        {
            Native.OS.OpenBrowser("https://github.com/Deftr0y/GitGator");
            e.Handled = true;
        }

        private void OnVisitSourceCode(object _, RoutedEventArgs e)
        {
            Native.OS.OpenBrowser("https://github.com/Deftr0y/GitGator");
            e.Handled = true;
        }

    }
}
