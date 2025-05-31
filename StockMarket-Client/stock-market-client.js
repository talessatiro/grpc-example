var PROTO_PATH = __dirname + "/protos/stock_market.proto";

var grpc = require("@grpc/grpc-js");
var protoLoader = require("@grpc/proto-loader");
var readline = require("readline");
var fs = require("node:fs");
var grpcStatus = require("grpc-error-status");

var packageDefinition = protoLoader.loadSync(PROTO_PATH, {
  keepCase: true,
  longs: Number,
  enums: String,
  defaults: true,
  oneofs: false,
});
var stocker_market_proto =
  grpc.loadPackageDefinition(packageDefinition).stock_market;

const handleError = (error) => {
  if (error.metadata.internalRepr.has("grpc-status-details-bin")) {
    var errorDetails = grpcStatus.parse(error).toObject();
    console.error(JSON.stringify(errorDetails, null, 2));
  } else {
    console.error(error);
  }
};

const executeUnaryCommunicationTest = (client) => {
  client.getStockPrice({ symbol: "PETR4" }, (err, response) => {
    if (err) {
      handleError(err);
    } else {
      console.log("[Unary]:", response);
    }
  });
};

const executeServerStreamingCommunicationTest = (client) => {
  var deadline = new Date();
  deadline.setSeconds(deadline.getSeconds() + 8);

  var serverStreamingCall = client.getStockPriceServerStreaming(
    {
      symbol: "PETR4",
    },
    {
      deadline,
    }
  );
  serverStreamingCall.on("data", (response) => {
    console.log("[Server Streaming Data]:", response);
  });
  serverStreamingCall.on("error", (error) => {
    handleError(error);
  });
  serverStreamingCall.on("status", (status) => {
    console.log("[Server Streaming Status]:", status);
  });
  serverStreamingCall.on("end", () => {
    console.log("[Server Streaming End]");
  });
};

const executeClientStreamingCommunicationTest = (client) => {
  var deadline = new Date();
  deadline.setSeconds(deadline.getSeconds() + 8);

  const clientStreamingCall = client.updateStockPriceClientStreaming(
    { deadline },
    (error, response) => {
      if (error) {
        handleError(error);
      } else {
        console.log("[Client Streaming Server Response]", response);
      }
    }
  );

  const fileStream = fs.createReadStream("data/stockprices.txt");
  const rd = readline.createInterface({ input: fileStream });

  rd.on("line", (line) => {
    clientStreamingCall.write(JSON.parse(line));
  });

  rd.on("close", () => {
    clientStreamingCall.end();
  });
};

const executeBidirectionalStreamingCommunicationTest = (client) => {
  const bidirectionalStreamingCall = client.getStockPriceBidirectionalStreaming(
    (error, response) => {
      if (error) {
        handleError(error);
      } else {
        console.log("[Client Streaming Server Response]", response);
      }
    }
  );

  bidirectionalStreamingCall.on("data", (response) => {
    console.log("[Bidirectional Streaming Data]:", response);
  });

  const rd = readline.createInterface({
    input: process.stdin,
    prompt: 'Digite o código ou "sair" para sair:',
    output: process.stdout,
  });

  rd.on("line", (symbol) => {
    if (symbol === "sair") {
      rd.close();
    } else {
      bidirectionalStreamingCall.write({ symbol });
    }
  });

  rd.on("close", () => {
    bidirectionalStreamingCall.end();
  });
};

const authInterceptor = (options, nextCall) => {
  var requester = new grpc.RequesterBuilder()
    .withStart((metadata, listener, next) => {
      metadata.set("Authorization", "jwt-token");
      next(metadata, listener);
    })
    .build();

  return new grpc.InterceptingCall(nextCall(options), requester);
};

const loggingInterceptor = (options, nextCall) => {
  var requester = new grpc.RequesterBuilder()
    .withStart((metadata, listener, next) => {
      listener = {
        onReceiveMessage: (message, next) => {
          console.log(`Intercepted message: ${JSON.stringify(message)}`);
          next(message);
        },
        onReceiveMetadata: (metadata, next) => {
          console.log(`Intercepted metadata: ${JSON.stringify(metadata)}`);
          next(metadata);
        },
        onReceiveStatus: (status, next) => {
          console.log(`Intercepted status: ${JSON.stringify(status)}`);
          next(status);
        },
      };

      console.log(`Client metadata: ${JSON.stringify(metadata)}`);
      next(metadata, listener);
    })
    .withSendMessage((message, next) => {
      console.log(`Send a message: ${JSON.stringify(message)}`);
      next(message);
    })
    .build();

  return new grpc.InterceptingCall(nextCall(options), requester);
};

const main = () => {
  var serviceConfig = fs.readFileSync("service-config.json", "utf-8");
  var client = new stocker_market_proto.StockPrice(
    "localhost:5251",
    grpc.credentials.createInsecure(),
    {
      "grpc.service_config": serviceConfig,
      interceptors: [authInterceptor, loggingInterceptor],
    }
  );

  // Unary
  executeUnaryCommunicationTest(client);

  // Server Streaming
  //executeServerStreamingCommunicationTest(client);

  // Client Streaming
  //executeClientStreamingCommunicationTest(client);

  // Bidirectional Streaming
  //executeBidirectionalStreamingCommunicationTest(client);
};

main();
