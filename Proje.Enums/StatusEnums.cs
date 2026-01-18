using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Proje.Enums
{
    public enum TransactionStatus
    {
        Onaylandi = 1,
        Reddedildi = 2,
        Beklemede = 3,
        Iptal = 4
    }

    public enum LoginStatus
    {
        Success = 1,
        Failed = 2,
        CaptchaRequired = 3,
        TwoFactorRequired = 4
    }

    public enum ExportType
    {
        Excel = 1,
        CSV = 2,
        PDF = 3
    }
}
