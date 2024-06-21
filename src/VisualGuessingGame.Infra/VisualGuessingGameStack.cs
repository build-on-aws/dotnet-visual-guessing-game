using Amazon.CDK;
using Amazon.CDK.AWS.APIGateway;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.Cognito.IdentityPool.Alpha;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.S3.Deployment;
using Amazon.CDK.CustomResources;
using Amazon.CloudFormation;
using Amazon.CloudFormation.Model;
using CargoLambda.CDK;
using Constructs;
using DotNext;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using AssetOptions = Amazon.CDK.AWS.S3.Assets.AssetOptions;
using BundlingOptions = Amazon.CDK.BundlingOptions;
using CloudFrontFunction = Amazon.CDK.AWS.CloudFront.Function;
using CloudFrontFunctionProps = Amazon.CDK.AWS.CloudFront.FunctionProps;
using LambdaFunction = Amazon.CDK.AWS.Lambda.Function;
using LambdaFunctionProps = Amazon.CDK.AWS.Lambda.FunctionProps;
using Stack = Amazon.CDK.Stack;

namespace VisualGuessingGame.Infra
{
    public class VisualGuessingGameStack : Stack
    {
        internal VisualGuessingGameStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            #region WEB_ClIENT
            // Create an Amazon S3 bucket to store the blazor webassembly spa content
            var webClientBucket = new Bucket(this, "WebClientBucket", new BucketProps()
            {
                AutoDeleteObjects = true,
                RemovalPolicy = RemovalPolicy.DESTROY,
                BlockPublicAccess = BlockPublicAccess.BLOCK_ALL
            });

            CloudFrontFunction spaRoutingFunction = new CloudFrontFunction(this, "WebClientSPARouting", new CloudFrontFunctionProps()
            {
                Runtime = FunctionRuntime.JS_2_0,
                Code = FunctionCode.FromInline("async function handler(event) {\r\n    const request = event.request;\r\n    const uri = request.uri;\r\n    if (uri !== \"/\" && uri !== \"\" && !uri.includes('.')) {\r\n        request.uri = '/index.html';\r\n    } \r\n\r\n    return request;\r\n}")
            });


            // Create an Amazon CloudFront distribution with the web client S3 bucket as origin
            var webClientDistribution = new Distribution(this, "WebClientDistribution", new DistributionProps()
            {
                DefaultBehavior = new BehaviorOptions()
                {
                    Origin = new S3Origin(webClientBucket),
                    CachePolicy = CachePolicy.CACHING_DISABLED,
                    FunctionAssociations = new [] { new FunctionAssociation(){ EventType = FunctionEventType.VIEWER_REQUEST, Function = spaRoutingFunction } }
                },
                DefaultRootObject = "index.html",
                PriceClass = PriceClass.PRICE_CLASS_100
            });
            
            IEnumerable<string> webClientPublishCommands = new[]
            {
                "dotnet publish -c Release",
                "cp -t /asset-output -R ./bin/Release/net8.0/publish/wwwroot/*"
            };

            var webClientDeployment = new BucketDeployment(this, "WebClientDeployment", new BucketDeploymentProps()
            {
                Sources = new []
                {
                    Source.Asset(Path.Join(Directory.GetCurrentDirectory(), "VisualGuessingGame.Client"),
                        new AssetOptions()
                        {
                            Bundling = new BundlingOptions()
                            {
                                Image = Runtime.DOTNET_8.BundlingImage,
                                Command = new string[]{"bash", "-c", string.Join(" && ", webClientPublishCommands)},
                                User = "root"
                            }
                        }
                        )
                },
                DestinationBucket = webClientBucket,
                MemoryLimit = 4096,
                Distribution = webClientDistribution,
                DistributionPaths = ["/*"]
            });

            new CfnOutput(this, "WebClientUrl", new CfnOutputProps()
            {
                Description = "Url to access the web client",
                Value = $"https://{webClientDistribution.DomainName}"
            });
            #endregion
            
            #region IDENTITY_PROVIDER

            var userPool = new UserPool(this, "IdP", new UserPoolProps()
            {
                SelfSignUpEnabled = true,
                SignInAliases = new SignInAliases()
                {
                    Email = false,
                    Username = true
                },
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            var demoUser = new CfnUserPoolUser(this, "DemoUser", new CfnUserPoolUserProps()
            {
                UserPoolId = userPool.UserPoolId,
                Username = "DemoUser"
            });


            // Don't do use this for real world application! It is a quick way to generate a roughly random password for a demo application!
            bool stackExist = true;
            string existingPassword = string.Empty;
            using (AmazonCloudFormationClient cfClient = new AmazonCloudFormationClient())
            {
                try
                {
                    var describeStacksAsyncTask = cfClient.DescribeStacksAsync(new DescribeStacksRequest()
                    {
                        StackName = this.StackName
                    });
                    describeStacksAsyncTask.Wait();
                    var stackDescription = describeStacksAsyncTask.Result;
                    var cfOutput = stackDescription.Stacks.First().Outputs.Where(x => x.OutputKey == "DemoUserPassword").First();
                    existingPassword= cfOutput.OutputValue;
                }
                catch(AggregateException e)
                {
                    if (e.InnerException is AmazonCloudFormationException && (e.InnerException as AmazonCloudFormationException).ErrorCode == "ValidationError")
                    {
                        stackExist = false;
                    }
                }
            }

            StringBuilder password = new StringBuilder();
            if (stackExist)
            {
                password.Append(existingPassword);
            }
            else
            {
                var random = new Random();
                password.Append(random.NextString("0123456789", 1));
                password.Append(random.NextString("abcdefghijklmnopqrstuvwxyz", 1));
                password.Append(random.NextString("ABCDEFGHIJKLMNOPQRSTUVWXYZ", 1));
                password.Append(random.NextString("^$*.[]{}()?-\"!@#%&/\\,><':;|_~`+=", 1));
                password.Append(random.NextString("0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ^$*.[]{}()?-\"!@#%&/\\,><':;|_~`+=", 8));
            }

            new CfnOutput(this, "DemoUserPassword", new CfnOutputProps()
            {
                Description = "The password set for DemoUser",
                Value = password.ToString()
            });

            // set the password only once at first deployment
            AwsCustomResource acrDemoUserSetPassword = new AwsCustomResource(this, "DemoUserSetPassword",
                new AwsCustomResourceProps()
                {
                    InstallLatestAwsSdk = false,
                    Policy = AwsCustomResourcePolicy.FromSdkCalls(new SdkCallsPolicyOptions()
                    {
                        Resources = [userPool.UserPoolArn]
                    }),
                    OnCreate = new AwsSdkCall()
                    {
                        Service = "CognitoIdentityServiceProvider",
                        Action = "adminSetUserPassword",
                        Parameters = new Dictionary<string, object>
                        {
                            {"Password", password.ToString()},
                            {"UserPoolId", userPool.UserPoolId },
                            {"Username", demoUser.Username },
                            {"Permanent", true }
                        },
                        PhysicalResourceId = PhysicalResourceId.Of(demoUser.Username + "SetPassword")
                    }
                });


            var clientApp = userPool.AddClient("VisualGuessingGameApp", new UserPoolClientOptions()
            {
                OAuth = new OAuthSettings()
                {
                    Flows = new OAuthFlows()
                    {
                        AuthorizationCodeGrant = true
                    },
                    CallbackUrls = ["https://localhost:7215", $"https://{webClientDistribution.DomainName}"],
                    LogoutUrls = ["https://localhost:7215", $"https://{webClientDistribution.DomainName}"]
                }
            });

            var userPoolDomain = userPool.AddDomain("Domain", new UserPoolDomainOptions()
            {
                CognitoDomain = new CognitoDomainOptions()
                {
                    DomainPrefix = this.Account + "-" + Names.UniqueResourceName(userPool, new UniqueResourceNameOptions() { MaxLength = 50 }).ToLower()
                }
            });

            var identityPool = new IdentityPool(this, "IdentityPool", new IdentityPoolProps()
            {
                AuthenticationProviders = new IdentityPoolAuthenticationProviders()
                {
                    UserPools = new IUserPoolAuthenticationProvider[]
                     {
                       new UserPoolAuthenticationProvider(new UserPoolAuthenticationProviderProps
                       {
                          UserPool = userPool,
                          UserPoolClient = clientApp
                       })
                    }
                }
            });

            identityPool.AuthenticatedRole.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("AmazonBedrockFullAccess"));
            #endregion

            #region IMAGE_STORAGE
            // Create an Amazon S3 bucket to store the generated images
            var imageStorageBucket = new Bucket(this, "ImageStorageBucket", new BucketProps()
            {
                AutoDeleteObjects = true,
                RemovalPolicy = RemovalPolicy.DESTROY,
                BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
                Cors = new CorsRule[]
                {
                    new CorsRule()
                    {
                        AllowedHeaders = new []{"*"},
                        AllowedMethods = new []{HttpMethods.GET},
                        AllowedOrigins = new []{$"https://{webClientDistribution.DomainName}, https://localhost:7215" },
                        ExposedHeaders = Array.Empty<string>(),
                    }
                }
            });
            imageStorageBucket.GrantReadWrite(identityPool.AuthenticatedRole);
            #endregion

            #region WEB_API

            string hostArchitecture = "x86_64";
            Architecture lambdaArchitecture = Architecture.X86_64;

            if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64)
            {
                hostArchitecture = "arm64";
                lambdaArchitecture = Architecture.ARM_64;
            }

            IEnumerable<string> commands = new[]
            {
                "dotnet tool install -g Amazon.Lambda.Tools",
                $"dotnet lambda package -farch {hostArchitecture}  -o /asset-output/output.zip --msbuild-parameters --self-contained"
            };

            var webApiFunction = new LambdaFunction(this, "WebAPIFunction", new LambdaFunctionProps()
            {
                Runtime = Runtime.DOTNET_8,
                Code = Code.FromAsset(Path.Join(Directory.GetCurrentDirectory(), "VisualGuessingGame.API"),
                    new AssetOptions()
                    {
                        Bundling = new BundlingOptions
                        {
                            Image = Runtime.DOTNET_8.BundlingImage,
                            Command = new string[]{"bash", "-c", string.Join(" && ", commands)}
                        }
                    }),
                Handler = "VisualGuessingGame.API",
                MemorySize = 2048,
                Timeout = Duration.Seconds(30),
                Architecture = lambdaArchitecture
            });

            var webApi = new LambdaRestApi(this, "WebAPI", new LambdaRestApiProps()
            {
                Handler = webApiFunction
            });

#pragma warning disable JSII001 // A required property is missing or null
            webClientDistribution.AddBehavior("config", new RestApiOrigin(webApi), new BehaviorOptions()
            {
                AllowedMethods = AllowedMethods.ALLOW_GET_HEAD,
                CachePolicy = CachePolicy.CACHING_DISABLED
            });
#pragma warning restore JSII001 // A required property is missing or null

#pragma warning disable JSII001 // A required property is missing or null
            webClientDistribution.AddBehavior("api/*", new RestApiOrigin(webApi), new BehaviorOptions()
            {
                AllowedMethods = AllowedMethods.ALLOW_ALL,
                CachePolicy = new CachePolicy(this, "ApiCachePolicy", new CachePolicyProps()
                {
                    CookieBehavior = CacheCookieBehavior.All(),
                    QueryStringBehavior = CacheQueryStringBehavior.All(),
                    HeaderBehavior = CacheHeaderBehavior.AllowList("Authorization", "Origin", "Referer"),
                    EnableAcceptEncodingBrotli = true,
                    EnableAcceptEncodingGzip = true,
                    DefaultTtl = Duration.Seconds(0),
                    MinTtl = Duration.Seconds(0),
                    MaxTtl = Duration.Seconds(1),
                })
            });
#pragma warning restore JSII001 // A required property is missing or null
            #endregion

            #region VECTOR_DATABASE
            var lanceDBBucket = new Bucket(this, "LanceDBBucket", new BucketProps()
            {
                AutoDeleteObjects = true,
                RemovalPolicy = RemovalPolicy.DESTROY,
                BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            });

            var lanceDBIndexFunction = new RustFunction(this, "LanceDBIndexFunction", new RustFunctionProps()
            {
                ManifestPath = Path.Join(Directory.GetCurrentDirectory(), "VisualGuessingGame.LanceDB.Index/Cargo.toml"),
                Runtime = Runtime.PROVIDED_AL2023.ToString(),
                MemorySize = 1024,
                Timeout = Duration.Seconds(10),
                Bundling = new CargoLambda.CDK.BundlingOptions()
                {
                    DockerImage = DockerImage.FromBuild(Path.Join(Directory.GetCurrentDirectory(), "VisualGuessingGame.Infra", "RustFunctions")),
                    ForcedDockerBundling = true,
                    Architecture = Architecture.X86_64
                },
                Environment = new Dictionary<string, string>()
                {
                    {"LANCEDB_BUCKET", lanceDBBucket.BucketName}
                }
            });

            lanceDBIndexFunction.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps()
            {
                Effect = Effect.ALLOW,
                Actions = new[] { "s3:ListBucket", "s3:GetBucketLocation" },
                Resources = new[] { lanceDBBucket.BucketArn },
            }));
            lanceDBIndexFunction.GrantInvoke(identityPool.AuthenticatedRole);
            lanceDBBucket.GrantReadWrite(lanceDBIndexFunction);


            var lanceDBQueryFunction = new RustFunction(this, "LanceDBQueryFunction", new RustFunctionProps()
            {
                ManifestPath = Path.Join(Directory.GetCurrentDirectory(), "VisualGuessingGame.LanceDB.Query/Cargo.toml"),
                Runtime = Runtime.PROVIDED_AL2023.ToString(),
                MemorySize = 1024,
                Timeout = Duration.Seconds(10),
                Bundling = new CargoLambda.CDK.BundlingOptions()
                {
                    DockerImage = DockerImage.FromBuild(Path.Join(Directory.GetCurrentDirectory(), "VisualGuessingGame.Infra", "RustFunctions")),
                    ForcedDockerBundling = true,
                    Architecture = Architecture.X86_64
                },
                Environment = new Dictionary<string, string>()
                {
                    {"LANCEDB_BUCKET", lanceDBBucket.BucketName}
                }
            });
            lanceDBQueryFunction.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps()
            {
                Effect = Effect.ALLOW,
                Actions = new[] { "s3:ListBucket", "s3:GetBucketLocation" },
                Resources = new[] { lanceDBBucket.BucketArn },
            }));
            lanceDBQueryFunction.GrantInvoke(identityPool.AuthenticatedRole);
            lanceDBBucket.GrantReadWrite(lanceDBQueryFunction);

            #endregion

            #region WEB_API_ENVIRONMENT_VARIABLE
            var acrWebApiEnvironmentVariables = new AwsCustomResource(this, "WebApiEnvironmentVariables",
                new AwsCustomResourceProps()
                {
                    InstallLatestAwsSdk = false,
                    Policy = AwsCustomResourcePolicy.FromSdkCalls(new SdkCallsPolicyOptions()
                    {
                        Resources = [webApiFunction.FunctionArn]
                    }),
                    OnCreate = new AwsSdkCall()
                    {
                        Service = "Lambda",
                        Action = "updateFunctionConfiguration",
                        Parameters = new Dictionary<string, object>
                        {
                                        {"FunctionName", webApiFunction.FunctionName},
                                        {
                                            "Environment", new Dictionary<string, object>()
                                            {
                                                {"Variables", new Dictionary<string, string>()
                                                {
                                                    { "Authority", userPool.UserPoolProviderUrl },
                                                    { "ClientId", clientApp.UserPoolClientId },
                                                    { "CognitoDomainName", userPoolDomain.DomainName },
                                                    { "CognitoRegion", userPool.Env.Region },
                                                    { "IdentityPoolId", identityPool.IdentityPoolId },
                                                    { "ImageStorageBucketName", imageStorageBucket.BucketName },
                                                    { "LanceDBIndexFunction", lanceDBIndexFunction.FunctionName },
                                                    { "LanceDBQueryFunction", lanceDBQueryFunction.FunctionName }
                                                }}
                                            }
                                        }
                        },
                        PhysicalResourceId = PhysicalResourceId.Of(webApiFunction.FunctionName + "EnvironmentVariables")
                    },
                    OnUpdate = new AwsSdkCall()
                    {
                        Service = "Lambda",
                        Action = "updateFunctionConfiguration",
                        Parameters = new Dictionary<string, object>
                        {
                                        {"FunctionName", webApiFunction.FunctionName},
                                        {
                                            "Environment", new Dictionary<string, object>()
                                            {
                                                {"Variables", new Dictionary<string, string>()
                                                {
                                                    { "Authority", userPool.UserPoolProviderUrl },
                                                    { "ClientId", clientApp.UserPoolClientId },
                                                    { "CognitoDomainName", userPoolDomain.DomainName},
                                                    { "CognitoRegion", userPool.Env.Region },
                                                    { "IdentityPoolId", identityPool.IdentityPoolId },
                                                    { "ImageStorageBucketName", imageStorageBucket.BucketName },
                                                    { "LanceDBIndexFunction", lanceDBIndexFunction.FunctionName },
                                                    { "LanceDBQueryFunction", lanceDBQueryFunction.FunctionName }
                                                }}
                                            }
                                        }
                        },
                        PhysicalResourceId = PhysicalResourceId.Of(webApiFunction.FunctionName + "EnvironmentVariables")
                    },
                    OnDelete = new AwsSdkCall()
                    {
                        Service = "Lambda",
                        Action = "updateFunctionConfiguration",
                        Parameters = new Dictionary<string, object>
                        {
                                        {"FunctionName", webApiFunction.FunctionName},
                                        {
                                            "Environment", new Dictionary<string, object>()
                                            {
                                                {"Variables", new Dictionary<string, string>()
                                                {
                                                }}
                                            }
                                        }
                        },
                        PhysicalResourceId = PhysicalResourceId.Of(webApiFunction.FunctionName + "EnvironmentVariables")
                    }
                });
            #endregion

            #region CFN_OUPUT_API_APPSETTINGS
            var authorityOutput = new CfnOutput(this, "AuthorityOutput", new CfnOutputProps()
            {
                Key = "Authority",
                Value = userPool.UserPoolProviderUrl
            });

            var clientIdOutput = new CfnOutput(this, "ClientIdOutput", new CfnOutputProps()
            {
                Key = "ClientId",
                Value = clientApp.UserPoolClientId
            });

            var cognitoDomainNameOutput = new CfnOutput(this, "CognitoDomainNameOutput", new CfnOutputProps()
            {
                Key = "CognitoDomainName",
                Value = userPoolDomain.DomainName
            });

            var cognitoRegionOutput = new CfnOutput(this, "CognitoRegionOutput", new CfnOutputProps()
            {
                Key = "CognitoRegion",
                Value = userPool.Env.Region
            });

            var identityPoolIdOutput = new CfnOutput(this, "IdentityPoolIdOutput", new CfnOutputProps()
            {
                Key = "IdentityPoolId",
                Value = identityPool.IdentityPoolId
            });

            var imageStorageBucketNameOutput = new CfnOutput(this, "ImageStorageBucketNameOutput", new CfnOutputProps()
            {
                Key = "ImageStorageBucketName",
                Value = imageStorageBucket.BucketName
            });

            var lanceDBIndexFunctionOutput = new CfnOutput(this, "LanceDBIndexFunctionOutput", new CfnOutputProps()
            {
                Key = "LanceDBIndexFunction",
                Value = lanceDBIndexFunction.FunctionName
            });

            var lanceDBQueryFunctionOutput = new CfnOutput(this, "LanceDBQueryFunctionOutput", new CfnOutputProps()
            {
                Key = "LanceDBQueryFunction",
                Value = lanceDBQueryFunction.FunctionName
            });
            #endregion

        }
    }

    public class RustCommandHook : CargoLambda.CDK.ICommandHooks
    {
        public string[] AfterBundling(string inputDir, string outputDir)
        {
            return [];
        }

        public string[] BeforeBundling(string inputDir, string outputDir)
        {
            return ["apt update", "apt install -y protobuf-compiler libssl-dev"];
        }
    }
}
