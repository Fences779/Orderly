namespace Orderly.App.ViewModels;

// Tracking-number carrier auto-detection. Pure UI-input convenience logic; does not
// touch the gateway, order service, or the frozen payment-to-fulfillment transaction loop.
public partial class MainViewModel
{
    partial void OnStringNarrationTrackingNoInputChanged(string value)
    {
        if (_isDetectingCarrier || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _isDetectingCarrier = true;
        try
        {
            AutoDetectCarrier(value);
        }
        finally
        {
            _isDetectingCarrier = false;
        }
    }

    private void AutoDetectCarrier(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        string normalized = input.Trim();
        string? detectedCarrier = null;
        string? detectedCode = null;
        string cleanTrackingNo = normalized;

        if (normalized.Contains("顺丰"))
        {
            detectedCarrier = "顺丰速运";
            detectedCode = "SF";
            cleanTrackingNo = normalized.Replace("顺丰", "").Replace("速运", "").Trim();
        }
        else if (normalized.Contains("京东"))
        {
            detectedCarrier = "京东快递";
            detectedCode = "JD";
            cleanTrackingNo = normalized.Replace("京东", "").Replace("快递", "").Trim();
        }
        else if (normalized.Contains("圆通"))
        {
            detectedCarrier = "圆通速递";
            detectedCode = "YTO";
            cleanTrackingNo = normalized.Replace("圆通", "").Replace("速递", "").Replace("快递", "").Trim();
        }
        else if (normalized.Contains("中通"))
        {
            detectedCarrier = "中通快递";
            detectedCode = "ZTO";
            cleanTrackingNo = normalized.Replace("中通", "").Replace("快递", "").Trim();
        }
        else if (normalized.Contains("申通"))
        {
            detectedCarrier = "申通快递";
            detectedCode = "STO";
            cleanTrackingNo = normalized.Replace("申通", "").Replace("快递", "").Trim();
        }
        else if (normalized.Contains("韵达"))
        {
            detectedCarrier = "韵达速递";
            detectedCode = "YUNDA";
            cleanTrackingNo = normalized.Replace("韵达", "").Replace("速递", "").Replace("快递", "").Trim();
        }
        else if (normalized.Contains("邮政") || normalized.Contains("挂号信"))
        {
            detectedCarrier = "邮政快递包裹";
            detectedCode = "POST";
            cleanTrackingNo = normalized.Replace("邮政", "").Replace("快递", "").Replace("包裹", "").Trim();
        }
        else if (normalized.Contains("极兔"))
        {
            detectedCarrier = "极兔速递";
            detectedCode = "JT";
            cleanTrackingNo = normalized.Replace("极兔", "").Replace("速递", "").Replace("快递", "").Trim();
        }
        else
        {
            string upper = normalized.ToUpperInvariant();
            if (upper.StartsWith("SF"))
            {
                detectedCarrier = "顺丰速运";
                detectedCode = "SF";
            }
            else if (upper.StartsWith("JD"))
            {
                detectedCarrier = "京东快递";
                detectedCode = "JD";
            }
            else if (upper.StartsWith("YT"))
            {
                detectedCarrier = "圆通速递";
                detectedCode = "YTO";
            }
            else if (upper.StartsWith("ZT"))
            {
                detectedCarrier = "中通快递";
                detectedCode = "ZTO";
            }
            else if (upper.StartsWith("ST"))
            {
                detectedCarrier = "申通快递";
                detectedCode = "STO";
            }
            else if (upper.StartsWith("YD"))
            {
                detectedCarrier = "韵达速递";
                detectedCode = "YUNDA";
            }
            else if (upper.StartsWith("JT"))
            {
                detectedCarrier = "极兔速递";
                detectedCode = "JT";
            }
        }

        if (detectedCarrier != null)
        {
            StringNarrationCarrierInput = detectedCarrier;
            StringNarrationExpressCompanyCodeInput = detectedCode ?? string.Empty;

            cleanTrackingNo = System.Text.RegularExpressions.Regex.Replace(cleanTrackingNo, @"^[^\w]+", "");
            cleanTrackingNo = System.Text.RegularExpressions.Regex.Replace(cleanTrackingNo, @"[^\w]+$", "");

            if (cleanTrackingNo != StringNarrationTrackingNoInput)
            {
                StringNarrationTrackingNoInput = cleanTrackingNo;
            }
        }
    }
}
