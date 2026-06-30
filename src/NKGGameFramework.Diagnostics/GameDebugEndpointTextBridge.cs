using System.Globalization;
using System.Text;

namespace NKGGameFramework.Diagnostics;

public sealed class GameDebugEndpointTextBridge : IDisposable
{
    private readonly GameDebugEndpointDispatcher _dispatcher;

    public GameDebugEndpointTextBridge(GameDebugEndpointDispatcherOptions? options = null)
        : this(new GameDebugEndpointDispatcher(options))
    {
    }

    public GameDebugEndpointTextBridge(GameDebugEndpointDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public string Handle(string request)
    {
        try
        {
            return FormatResponse(_dispatcher.Handle(ParseRequest(request)));
        }
        catch (InvalidDataException exception)
        {
            return FormatErrorResponse(400, "Bad Request", exception.Message);
        }
        catch (Exception exception)
        {
            return FormatErrorResponse(500, "Internal Server Error", FormatException(exception));
        }
    }

    public void Dispose()
    {
        _dispatcher.Dispose();
    }

    private static GameDebugEndpointRequest ParseRequest(string request)
    {
        var firstBreak = request.IndexOf('\n', StringComparison.Ordinal);
        if (firstBreak < 0)
        {
            throw new InvalidDataException("The native debug bridge request was malformed.");
        }

        var secondBreak = request.IndexOf('\n', firstBreak + 1);
        if (secondBreak < 0)
        {
            throw new InvalidDataException("The native debug bridge request target was missing.");
        }

        var thirdBreak = request.IndexOf('\n', secondBreak + 1);
        var method = request[..firstBreak].Trim();
        var target = request[(firstBreak + 1)..secondBreak].Trim();
        if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidDataException("The native debug bridge request line was empty.");
        }

        if (thirdBreak >= 0)
        {
            var encoding = request[(secondBreak + 1)..thirdBreak].Trim();
            var encodedBody = thirdBreak + 1 < request.Length ? request[(thirdBreak + 1)..] : string.Empty;
            if (StringComparer.OrdinalIgnoreCase.Equals(encoding, "base64"))
            {
                return new GameDebugEndpointRequest(
                    method.ToUpperInvariant(),
                    target,
                    string.IsNullOrEmpty(encodedBody) ? [] : Convert.FromBase64String(encodedBody));
            }
        }

        var legacyBody = secondBreak + 1 < request.Length ? request[(secondBreak + 1)..] : string.Empty;
        return new GameDebugEndpointRequest(
            method.ToUpperInvariant(),
            target,
            Encoding.UTF8.GetBytes(legacyBody));
    }

    private static string FormatResponse(GameDebugEndpointResponse response)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{response.StatusCode}\n{response.ReasonPhrase}\n{response.ContentType}\n{response.BodyText}");
    }

    private static string FormatErrorResponse(int statusCode, string reasonPhrase, string message)
    {
        return FormatResponse(new GameDebugEndpointResponse(
            statusCode,
            reasonPhrase,
            "application/json; charset=utf-8",
            GameDebugEndpointLeanJson.SerializeError(message)));
    }

    private static string FormatException(Exception exception)
    {
        var builder = new StringBuilder();
        AppendExceptionSummary(builder, exception);

        if (exception.InnerException is { } inner)
        {
            builder.Append(" | inner: ");
            AppendExceptionSummary(builder, inner);
        }

        if (!string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            builder.Append(" | stack: ")
                .Append(exception.StackTrace);
        }

        return builder.ToString();
    }

    private static void AppendExceptionSummary(StringBuilder builder, Exception exception)
    {
        var typeName = exception.GetType().FullName ?? exception.GetType().Name;
        builder.Append(typeName);
        if (StringComparer.Ordinal.Equals(typeName, "System.IO.FileLoadException"))
        {
            return;
        }

        builder.Append(": ").Append(exception.Message);
    }
}
