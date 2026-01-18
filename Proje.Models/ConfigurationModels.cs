using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Proje.Models
{
    public class LoginCredentials
    {
        public string BasicAuthUsername { get; set; }
        public string BasicAuthPassword { get; set; }
        public string FormUsername { get; set; }
        public string FormPassword { get; set; }
        public string LoginUrl { get; set; }
    }

    public class ExcelConfig
    {
        public string FilePath { get; set; }
        public string SheetName { get; set; }
        public int StartRow { get; set; } = 2;
        public Dictionary<string, int> ColumnMappings { get; set; }
    }

    public class BrowserConfig
    {
        public bool Headless { get; set; } = false;
        public int TimeoutSeconds { get; set; } = 30;
        public string UserAgent { get; set; }
        public int MaxRetryCount { get; set; } = 3;
    }
}
