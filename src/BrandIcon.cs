using System.Drawing;
using System.IO;
using System.Reflection;

namespace CuteClash
{
    /// <summary>The application and notification-area icon, owned by its caller.</summary>
    public static class BrandIcon
    {
        public static Icon Load()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CuteClash.AppIcon"))
            {
                if (stream == null) return (Icon)SystemIcons.Application.Clone();
                using (var icon = new Icon(stream)) return (Icon)icon.Clone();
            }
        }
    }
}
