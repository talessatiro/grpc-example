using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using StockMarket.Protos;
using Status = Grpc.Core.Status;

namespace StockMarket.Services;

public class StockPriceService(ILogger<StockPriceService> logger) : StockPrice.StockPriceBase
{
    private readonly DateTime _openMarket = DateTime.UtcNow.Date.AddHours(9).AddMinutes(30);
    private readonly DateTime _closeMarket = DateTime.UtcNow.Date.AddHours(17);
    private static int _requestCount = 0;


    #region Public Methods

    public override async Task<StockResponse> GetStockPrice(StockRequest request, ServerCallContext context)
    {
        EnsureMarketIsOpen();

        var random = new Random();
        var price = Math.Round(random.NextDouble() * (300 - 50) + 50, 2);

        var response = new StockResponse
        {
            Symbol = request.Symbol,
            Price = price,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        logger.LogInformation("Checking the price for {symbol}: $ {price}", request.Symbol, price);

        return response;
    }

    public override async Task GetStockPriceServerStreaming(StockRequest request,
        IServerStreamWriter<StockResponse> responseStream, ServerCallContext context)
    {
        var maxNotifications = 10;
        var notificationsQuantity = 0;
        var deadline = context.Deadline.AddSeconds(-3);

        if (context.Deadline < DateTime.UtcNow)
        {
            throw new RpcException(new Status(StatusCode.Cancelled, "Invalid Deadline Value!"));
        }

        while (!context.CancellationToken.IsCancellationRequested && notificationsQuantity < maxNotifications &&
               DateTime.UtcNow < deadline)
        {
            await responseStream.WriteAsync(await GetStockPrice(request, context));
            await Task.Delay(TimeSpan.FromSeconds(2));
            notificationsQuantity++;
        }
    }

    public override async Task<UpdateStockPriceBatchResponse> UpdateStockPriceClientStreaming(
        IAsyncStreamReader<UpdateStockPriceRequest> requestStream, ServerCallContext context)
    {
        var prices = new List<string>();
        var batchSize = 2;
        var countMessage = 0;
        while (await requestStream.MoveNext(context.CancellationToken))
        {
            EnsureMarketIsOpen();
            var stock = requestStream.Current;
            ValidateStockPrice(stock);

            logger.LogInformation("New price for {symbol}: $ {price}", stock.Symbol, stock.Price);
            prices.Add(JsonSerializer.Serialize(stock));
            countMessage++;
            if (countMessage % batchSize == 0)
            {
                logger.LogInformation("Saving Batch!");
                await File.AppendAllLinesAsync("stockprices.txt", prices, context.CancellationToken);
                prices.Clear();
            }
        }

        await File.AppendAllLinesAsync("stockprices.txt", prices, context.CancellationToken);

        return new UpdateStockPriceBatchResponse
        {
            Message = $"{countMessage} updated stocks!"
        };
    }

    public override async Task GetStockPriceBidirectionalStreaming(IAsyncStreamReader<StockRequest> requestStream,
        IServerStreamWriter<StockResponse> responseStream,
        ServerCallContext context)
    {
        while (await requestStream.MoveNext(context.CancellationToken))
        {
            var stock = requestStream.Current;
            await responseStream.WriteAsync(await GetStockPrice(stock, context));
        }
    }

    #endregion

    #region Private Methods

    private void SimulateSolutionErrors()
    {
        _requestCount++;
        if (_requestCount <= 4)
        {
            logger.LogInformation("Request {count}: {time}", _requestCount, DateTime.UtcNow.TimeOfDay);

            switch (_requestCount)
            {
                case 1:
                    throw new RpcException(new Status(StatusCode.Unavailable, "Unavailable Service!"));
                case 2:
                    throw new RpcException(new Status(StatusCode.Internal, "Internal Error!"));
                case 3:
                    throw new RpcException(new Status(StatusCode.Aborted, "Aborted with conflict!"));
                default:
                    throw new RpcException(new Status(StatusCode.Unknown, "Unknown Error!"));
            }
        }

        _requestCount = 0;
    }

    private void EnsureMarketIsOpen()
    {
        // Uncomment to simulate solution errors (Should be used to validate retries policies)
        // SimulateSolutionErrors();

        // if (DateTime.UtcNow < _openMarket || DateTime.UtcNow > _closeMarket)
        // {
        //     throw new Google.Rpc.Status
        //     {
        //         Code = (int)Code.FailedPrecondition,
        //         Message = "A bolsa de valores está fechada!",
        //         Details =
        //         {
        //             Any.Pack(new PreconditionFailure
        //             {
        //                 Violations =
        //                 {
        //                     new PreconditionFailure.Types.Violation
        //                     {
        //                         Type = "INVALID_OPERATION_TIME",
        //                         Subject = "Stock Market",
        //                         Description = "The market just works between 9:30 and 17:00."
        //                     }
        //                 }
        //             })
        //         }
        //     }.ToRpcException();
        // }
    }

    private void ValidateStockPrice(UpdateStockPriceRequest stockPriceRequest)
    {
        List<BadRequest.Types.FieldViolation> violations = new List<BadRequest.Types.FieldViolation>();
        if (stockPriceRequest.Price < 0)
        {
            violations.Add(new BadRequest.Types.FieldViolation
            {
                Field = nameof(UpdateStockPriceRequest.Price),
                Description = "The stock price cannot be lees than zero."
            });
        }

        if (stockPriceRequest.Symbol.Length < 4 || stockPriceRequest.Symbol.Length > 5)
        {
            violations.Add(new BadRequest.Types.FieldViolation
            {
                Field = nameof(UpdateStockPriceRequest.Price),
                Description = "The stock code must have 4 or 5 characters."
            });
        }

        if (violations.Any())
        {
            throw new Google.Rpc.Status
            {
                Code = (int)Code.InvalidArgument,
                Message = "Invalid Request!",
                Details =
                {
                    Any.Pack(new BadRequest
                    {
                        FieldViolations = { violations }
                    })
                }
            }.ToRpcException();
        }
    }

    #endregion
}