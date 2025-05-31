using Grpc.Core;
using Grpc.Core.Interceptors;

namespace StockMarket.Interceptors;

public class AddTraceIdInterceptor : Interceptor
{
    private readonly string TRACE_ID_HEADER = "x-trace-id";

    #region Public Methods

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        AddTraceId(context);

        return await continuation(request, context);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        AddTraceId(context);

        await continuation(request, responseStream, context);
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream, ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        AddTraceId(context);

        return await continuation(requestStream, context);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        AddTraceId(context);

        await continuation(requestStream, responseStream, context);
    }

    #endregion

    #region Private Methods

    private void AddTraceId(ServerCallContext context)
    {
        var traceId = context.RequestHeaders.GetValue(TRACE_ID_HEADER);

        if (traceId == null)
        {
            traceId = Guid.NewGuid().ToString();
            context.ResponseTrailers.Add(TRACE_ID_HEADER, traceId);
        }
    }

    #endregion
}