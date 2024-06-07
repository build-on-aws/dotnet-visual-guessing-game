use arrow_array::{Array, StringArray};
use lambda_runtime::{run, service_fn, tracing, Error, LambdaEvent};
use lancedb::connect;
use lancedb::query::{ExecutableQuery, QueryBase};
use futures::TryStreamExt;
use serde::{Deserialize, Serialize};

/// This is a made-up example. Requests come into the runtime as unicode
/// strings in json format, which can map to any structure that implements `serde::Deserialize`
/// The runtime pays no attention to the contents of the request payload.
#[derive(Deserialize)]
struct Request {
    collection: String,
    vector: Vec<f32>,
}

/// This is a made-up example of what a response structure may look like.
/// There is no restriction on what it can be. The runtime requires responses
/// to be serialized into json. The runtime pays no attention
/// to the contents of the response payload.
#[derive(Serialize)]
struct Response {
    req_id: String,
    top1: String,
    top2: String,
}

/// This is the main body for the function.
/// Write your code inside it.
/// There are some code example in the following URLs:
/// - https://github.com/awslabs/aws-lambda-rust-runtime/tree/main/examples
/// - https://github.com/aws-samples/serverless-rust-demo/
async fn function_handler(event: LambdaEvent<Request>) -> Result<Response, Error> {
    println!("Function {:?} invoked with request id {:?}", event.context.env_config.function_name, event.context.request_id);
    let s3bucket_name = std::env::var("LANCEDB_BUCKET").expect("LANCEDB_BUCKET must be set");
    println!("Try to connect to {} as Lance DB storage", s3bucket_name);
    let client_result = connect(format!("s3://{s3bucket_name}/lancedb/").as_str()).execute().await;
    let client = match client_result
    {
        Ok(client) => {
            println!("Connected to storage");
            client
        },
        Err(e) =>
            {
                panic!("Unable to connect to storage due to error: {e}")
            }
    };

    let collection = event.payload.collection;
    println!("Try to open {} table", collection);
    let open_table_result = client.open_table(collection.clone()).execute().await;

    let table = match open_table_result
    {
        Ok(table) => {
            println!("Connected to table");
            table
        }
        Err(e) => panic!("Can't open the table with error {e}")
    };

    let vector = event.payload.vector;

    let stream = table
        .query()
        .limit(2)
        .nearest_to(vector)
        .unwrap()
        .execute()
        .await?
        .try_collect::<Vec<_>>()
        .await?;

    let first_res = stream.get(0).unwrap();
    println!("Succeed to get RecordBatch {:?}", first_res);

    let name_column_data = first_res.column(1).as_any().downcast_ref::<StringArray>().unwrap();
    println!("Top 1 response {}", name_column_data.value(0));
    println!("Top 2 response {}", name_column_data.value(1));

    // Prepare the response
    let resp = Response {
        req_id: event.context.request_id,
        top1: name_column_data.value(0).to_string(),
        top2: name_column_data.value(1).to_string()
    };

    // Return `Response` (it will be serialized to JSON automatically by the runtime)
    Ok(resp)
}

#[tokio::main]
async fn main() -> Result<(), Error> {
    tracing::init_default_subscriber();

    run(service_fn(function_handler)).await
}
