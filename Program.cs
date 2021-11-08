// ReSharper disable UnassignedField.Global
// ReSharper disable JoinDeclarationAndInitializer

namespace LycamobileBot {

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DotNetEnv;

public static class HttpStatusCodeExtensions {
    public static bool isOk( this HttpStatusCode httpStatusCode )
        => httpStatusCode == HttpStatusCode.OK;
}

public class UriRelative : Uri {
    public UriRelative( string uriString ) : base( uriString, UriKind.Relative ) {
    }
}

public record Usage {
    public UsageData data;
    public int code;
    public string message;
}

public record UsageData {
    public string number;
    public string remaining;
    public string until;

    public override string ToString()
        => $"{number}\n"
            + $"remaining: {remaining}\n"
            + $"{until}\n";
}

public static class DebuggingExtensions {
    public static string toString<T>( this IEnumerable<T> iEnumerable )
        => string.Join( ",", iEnumerable );

    public static string toString( this ParameterInfo parameterInfo )
        => $"{parameterInfo.ParameterType.Name} {parameterInfo.Name}";

    public static string toString( this MethodBase methodBase ) {
        return $"{methodBase.Name}({methodBase.GetParameters().Select( p => p.toString() ).toString()})";
    }

    public static string toString( this StackFrame stackFrame )
        => stackFrame == null
            ? "NO STACK FRAME!"
            : $"{stackFrame.GetMethod().toString()}"
            + $" @ {stackFrame.GetFileName()} : L{stackFrame.GetFileLineNumber()}:C{stackFrame.GetFileColumnNumber()}";

    public static StackFrame getFrame( this Exception ex, int index )
        => new StackTrace( ex, true ).GetFrame( index );
}

public class Program {
    private const string BASE_ADDRESS = "https://www.lycamobile.se/";
    private static readonly HttpClient httpClient = new();

    private static Dictionary<string, string> env;

    private static readonly string appName = AppDomain.CurrentDomain.FriendlyName;

    [STAThread]
    private static void Main( string[] args ) {
        Form mainForm = null;
        try {
            mainForm = new() {
                Icon = Icon.ExtractAssociatedIcon( Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty ),
                Size = new( 0, 0 ),
                StartPosition = FormStartPosition.Manual,
                Location = new( 0, 0 ),
                Text = appName,
            };
            mainForm.Show();
            runApp( mainForm );
        } catch ( Exception ex ) {
            MessageBox.Show(
                mainForm,
                ex.Message,
                $"{appName} @ {ex.getFrame( 0 ).toString()}",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    private static void runApp( Form mainForm ) {
        env = Env.TraversePath().NoEnvVars().Load().ToDictionary();

        httpClient.Timeout = TimeSpan.FromSeconds( 10 );
        httpClient.BaseAddress = new( BASE_ADDRESS );
        HttpRequestHeaders defaultRequestHeaders = httpClient.DefaultRequestHeaders;
        defaultRequestHeaders.Add( "X-Requested-With", "XMLHttpRequest" );

        DialogResult dialogResult;
        do {
            string[] numbers = env["NUMBER"].Split( ',' );
            string usageText = numbers.Select(
                n => "" + fetchUsage( n, env["PASSWORD"] ).data
            ).Aggregate(
                ( a, b ) => $"{a}\n{b}"
            );

            Console.WriteLine( usageText );

            dialogResult = MessageBox.Show(
                mainForm,
                usageText,
                appName,
                MessageBoxButtons.RetryCancel,
                MessageBoxIcon.Information
            );
        } while ( dialogResult == DialogResult.Retry );

        // end
    }

    private static Usage fetchUsage( string number, string password ) {
        HttpRequestMessage requestMessage;
        HttpResponseMessage responseMessage;

        requestMessage = new() {
            RequestUri = new UriRelative( "wp-admin/admin-ajax.php" ),
            Method = HttpMethod.Post,
            Content = new FormUrlEncodedContent( new Dictionary<string, string>() {
                ["action"] = "lyca_login_ajax",
                ["method"] = "login",
                ["mobile_no"] = number,
                ["pass"] = password,
            } ),
        };
        responseMessage = httpClient.Send( requestMessage );
        if ( !responseMessage.StatusCode.isOk() ) {
            throw new HttpRequestException( responseMessage.ToString() );
        }

        requestMessage = new() {
            RequestUri = new UriRelative( "en/my-account/" ),
        };
        responseMessage = httpClient.Send( requestMessage );

        if ( !responseMessage.StatusCode.isOk() ) {
            throw new HttpRequestException( responseMessage.ToString() );
        }

        string myAccountHtml = responseMessage.Content.ReadAsStringAsync().Result;

        Regex regexRemaining = new(
            "<span>\\| International(Valid till [0-9]{2}-[0-9]{2}-[0-9]{4})<\\/span>" +
            ".*" +
            "<div class=\"bdl-mins\">\\s*([0-9]*.[0-9]*[A-Z]*)<\\/div>",
            RegexOptions.Compiled | RegexOptions.Singleline );

        GroupCollection groups = regexRemaining.Match( myAccountHtml ).Groups;

        if ( groups.Count < 3 ) {
            throw new HttpRequestException( "can't parse response HTML" );
        }

        Usage usage = new() {
            data = new() {
                number = "+" + number,
                until = groups[1].Value,
                remaining = groups[2].Value,
            },
        };

        return usage;
    }
}

}
