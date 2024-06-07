use std::sync::Arc;
use arrow_array::{FixedSizeListArray, RecordBatch, RecordBatchIterator, StringArray};
use arrow_array::types::Float32Type;
use lambda_runtime::{run, service_fn, tracing, Error, LambdaEvent};
use lancedb::arrow::arrow_schema::{DataType, Field, Schema};
use lancedb::connect;

use serde::{Deserialize, Serialize};

/// This is a made-up example. Requests come into the runtime as unicode
/// strings in json format, which can map to any structure that implements `serde::Deserialize`
/// The runtime pays no attention to the contents of the request payload.
#[derive(Deserialize)]
struct Request {
    collection: String,
    vector: Vec<Option<f32>>,
    image_location:  String,
    image_description: String
}

/// This is a made-up example of what a response structure may look like.
/// There is no restriction on what it can be. The runtime requires responses
/// to be serialized into json. The runtime pays no attention
/// to the contents of the response payload.
#[derive(Serialize)]
struct Response {
    req_id: String,
    msg: String,
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

    let schema = Arc::new(Schema::new(vec![
        Field::new("vector", DataType::FixedSizeList(Arc::new(Field::new("item", DataType::Float32, true)), 1024), false),
        Field::new("image_location", DataType::Utf8, false),
        Field::new("image_description", DataType::Utf8, false),
    ]));

    let collection = event.payload.collection;
    println!("Try to open {} table", collection);
    let open_table_result = client.open_table(collection.clone()).execute().await;

    let table = match open_table_result
    {
        Ok(table) => {
            println!("Connected to table");
            table
        },
        Err(lancedb::Error::TableNotFound{name:_}) => {
            match client.create_empty_table(collection.clone(), schema.clone()).execute().await
                {
                    Ok(table) => {
                        println!("Connected to table");
                        table
                    },
                    Err(ref e) => {
                        panic!("Error while creating table: {e}")
                    }
                }
        },
        Err(e) => panic!("Can't open the table with error {e}")
    };


    let vector = event.payload.vector;
    let image_location = event.payload.image_location;
    let image_description = event.payload.image_description;

    let vector_batch_iterator = FixedSizeListArray::from_iter_primitive::<Float32Type, _,_>([Some(vector)], 1024);
    let batch = RecordBatch::try_new(schema.clone(), vec![Arc::new(vector_batch_iterator), Arc::new(StringArray::from(vec![image_location])), Arc::new(StringArray::from(vec![image_description]))]);

    println!("Try to add to table");
    let add_result = table.add(RecordBatchIterator::new(vec![batch], schema.clone())).execute().await;

    match add_result {
        Ok(_) => println!("Added to table"),
        Err(e) => panic!("Error while adding to table: {e}")
    }

    // Prepare the response
    let resp = Response {
        req_id: event.context.request_id,
        msg: format!("Collection {}.", collection),
    };

    // Return `Response` (it will be serialized to JSON automatically by the runtime)
    Ok(resp)
}

#[tokio::main]
async fn main() -> Result<(), Error> {
    tracing::init_default_subscriber();

    run(service_fn(function_handler)).await
}
