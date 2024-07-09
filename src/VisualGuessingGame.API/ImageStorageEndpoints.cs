using Amazon.CognitoIdentity;
using Amazon.BedrockRuntime;
using Rockhead.Extensions;
using Rockhead.Extensions.Amazon;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Amazon.BedrockRuntime.Model;
using Amazon.S3;
using Amazon.Util;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections;
using Amazon.S3.Model;

namespace VisualGuessingGame.API
{
    public static class ImageStorageEndpoints
    {
        public static void RegisterImageStorageEndpoints(this WebApplication app)
        {
            app.MapGet("api/imagestorage/collections", GetImageStorageCollectionsRequest).RequireAuthorization();
            app.MapPost("api/imagestorage/collections/{collectionName}", PostImageStorageCollectionsRequest).RequireAuthorization();
            app.MapGet("api/imagestorage/collections/{collectionName}", GetImagesPresignedUrlsRequest).RequireAuthorization();
            app.MapGet("api/imagestorage/collections/{collectionName}/{imageName}", GetImagePresignedUrlRequest).RequireAuthorization();
        }

        private static async Task<IResult> GetImagePresignedUrlRequest(IConfiguration configuration, HttpContext context, string collectionName, string imageName)
        {
            CognitoAWSCredentials credentials = new CognitoAWSCredentials(configuration["IdentityPoolId"], Amazon.RegionEndpoint.GetBySystemName(configuration["CognitoRegion"]));
            var authority = configuration["Authority"]?.Replace("https://", "");
            var access_token = await context.GetTokenAsync("access_token");
            credentials.AddLogin(authority, access_token);

            AmazonS3Client client = new AmazonS3Client(credentials);

            var response = await client.ListObjectsAsync(new ListObjectsRequest()
            {
                BucketName = configuration["ImageStorageBucketName"],
                Prefix = collectionName
            });

            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                return Results.Problem("An error has occured while retrieving the collection images");
            }

            var preSignedUrl = await client.GetPreSignedURLAsync(new GetPreSignedUrlRequest()
            {
                BucketName = configuration["ImageStorageBucketName"],
                Key = $"{collectionName}/{imageName}",
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.AddSeconds(3600)
            });

            return Results.Ok<ImagePreSignedUrl>(new ImagePreSignedUrl(collectionName, imageName, preSignedUrl));
        }

        private static async Task<IResult> GetImagesPresignedUrlsRequest(IConfiguration configuration, HttpContext context, string collectionName)
        {
            CognitoAWSCredentials credentials = new CognitoAWSCredentials(configuration["IdentityPoolId"], Amazon.RegionEndpoint.GetBySystemName(configuration["CognitoRegion"]));
            var authority = configuration["Authority"]?.Replace("https://", "");
            var access_token = await context.GetTokenAsync("access_token");
            credentials.AddLogin(authority, access_token);

            AmazonS3Client client = new AmazonS3Client(credentials);

            var response = await client.ListObjectsAsync(new ListObjectsRequest()
            {
                BucketName = configuration["ImageStorageBucketName"],
                Prefix = collectionName
            });

            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                return Results.Problem("An error has occured while retrieving the collection images");
            }

            List<ImagePreSignedUrl> preSignedUrls = new List<ImagePreSignedUrl>();

            await Parallel.ForEachAsync<S3Object>(response.S3Objects,
                async ValueTask (s3Object, cancellationToken) =>
                {
                    var preSignedUrl = await client.GetPreSignedURLAsync(new GetPreSignedUrlRequest()
                    {
                        BucketName = configuration["ImageStorageBucketName"],
                        Key = s3Object.Key,
                        Verb = HttpVerb.GET,
                        Expires = DateTime.UtcNow.AddSeconds(600)
                    });
                    preSignedUrls.Add(new ImagePreSignedUrl(collectionName, s3Object.Key.Remove(0, $"{collectionName}/".Length), preSignedUrl));
                });
            
            return Results.Ok<IEnumerable<ImagePreSignedUrl>>(preSignedUrls);
        }

        private static async Task<IResult> PostImageStorageCollectionsRequest(IConfiguration configuration, HttpContext context, string collectionName, [FromBody] ImageStorageParam param)
        {
            CognitoAWSCredentials credentials = new CognitoAWSCredentials(configuration["IdentityPoolId"], Amazon.RegionEndpoint.GetBySystemName(configuration["CognitoRegion"]));
            var authority = configuration["Authority"]?.Replace("https://", "");
            var access_token = await context.GetTokenAsync("access_token");
            credentials.AddLogin(authority, access_token);

            AmazonS3Client client = new AmazonS3Client(credentials);
            var key = $"{collectionName}/{Guid.NewGuid().ToString().ToLower()}.jpeg";

            var response = await client.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest()
            {
                BucketName = configuration["ImageStorageBucketName"],
                Key = key,
                ContentType = "image/jpeg",
                InputStream = new MemoryStream(Convert.FromBase64String(param.Image))
            });

            return Results.Ok<string>(key);
        }

            private static async Task<IResult> GetImageStorageCollectionsRequest(IConfiguration configuration, HttpContext context)
        {
            CognitoAWSCredentials credentials = new CognitoAWSCredentials(configuration["IdentityPoolId"], Amazon.RegionEndpoint.GetBySystemName(configuration["CognitoRegion"]));
            var authority = configuration["Authority"]?.Replace("https://", "");
            var access_token = await context.GetTokenAsync("access_token");
            credentials.AddLogin(authority, access_token);

            AmazonS3Client client = new AmazonS3Client(credentials);
            try
            {
                var response = await client.ListObjectsAsync(new ListObjectsRequest()
                {
                        BucketName = configuration["ImageStorageBucketName"],
                        Delimiter = "/"
                });

                if(response.HttpStatusCode != System.Net.HttpStatusCode.OK)
                {
                    return Results.Problem("An error has occured while retrieving the existing collection");
                }

                var imageCollectionNames = new List<string>();

                foreach(var commonPrefix in response.CommonPrefixes)
                {
                    imageCollectionNames.Add(commonPrefix.TrimEnd('/'));
                }

                return Results.Ok<IEnumerable<string>>(imageCollectionNames);
            }
            catch(Exception)
            {
                return Results.Problem("An error has occured while retrieving the existing collection");
            }
         }

        public record ImageStorageParam(string Image);

        public record ImagePreSignedUrl(string CollectionName, string ImageName, string PreSignedUrl);
    }
}
