using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

internal sealed class FirmwareResult
{
    public string Published, Summary;
    public DateTime CheckedAt;
}

internal static class FirmwareUpdates
{
    public const string NotesUrl="https://support.logi.com/hc/en-us/articles/360048967733-G-HUB-Update-Release-Notes";
    public const string UpdaterUrl="https://www.logitechg.com/en-us/software/ghub";
    public static FirmwareResult Parse(string html,string installed)
    {
        string plain=WebUtility.HtmlDecode(Regex.Replace(html,"<[^>]+>"," "));
        var matches=Regex.Matches(plain,@"PRO\s+X\s+SUPERLIGHT\s+2\s*\(\s*Mouse\s+ver[:.]\s*(\d+\.\d+\.\d+)",RegexOptions.IgnoreCase);
        Version best=null;
        foreach(Match match in matches)
        {
            Version candidate;
            if(Version.TryParse(match.Groups[1].Value,out candidate) && (best==null || candidate>best))best=candidate;
        }
        if(best==null)throw new InvalidDataException("Logitech's release notes did not contain a recognizable firmware version for this exact mouse. Latest status is unknown.");
        Version local;
        if(!Version.TryParse(installed,out local))throw new InvalidDataException("Read the mouse's installed firmware before checking updates.");
        string summary=local<best?"Newer published firmware available":local==best?"Matches Logitech's published version":"Installed version is newer than the published notes";
        return new FirmwareResult{Published=best.ToString(),Summary=summary,CheckedAt=DateTime.Now};
    }
    public static async Task<FirmwareResult> Check(string installed,CancellationToken cancellation)
    {
        // The legacy compiler targets an older runtime default; require modern TLS.
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
            timeout.CancelAfter(15000);
            var request=(HttpWebRequest)WebRequest.Create(NotesUrl);
            request.AllowAutoRedirect=false;
            request.UserAgent="MouseController/0.2";
            request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
            request.Timeout=15000;request.ReadWriteTimeout=15000;
            using(timeout.Token.Register(()=>request.Abort()))
            using(var response=(HttpWebResponse)await request.GetResponseAsync())
            {
                if(response.StatusCode!=HttpStatusCode.OK)throw new IOException("Logitech's update page could not be checked. Try again later.");
                using(var stream=response.GetResponseStream())
                using(var reader=new StreamReader(stream))
                {
                    char[] buffer=new char[8192];var body=new System.Text.StringBuilder();int count;
                    while((count=await reader.ReadAsync(buffer,0,buffer.Length))>0)
                    {
                        timeout.Token.ThrowIfCancellationRequested();
                        if(body.Length+count>2000000)throw new IOException("Update response exceeded the size limit.");
                        body.Append(buffer,0,count);
                    }
                    return Parse(body.ToString(),installed);
                }
            }
        }
    }
}
