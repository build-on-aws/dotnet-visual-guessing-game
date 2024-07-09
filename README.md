## Visual Guessing Game

Visual Guessing Game is a .NET demonstration application. It showcases how to leverage multiple Large Language Models (LLMs) and vector databases to deliver new experience to users.
It is written mainly in C# using Blazor WebAssembly for the frontend web application and ASP.NET Core for the backend API. It also uses LanceDB as its vector databases. Thus, it is leverage a bit of Rust too.

## How it works

The general idea of visual guessing game is as follow:
1. Players select a game
1. They are presented a set of images
1. They pick one and provide a textual description
1. Based on the provided description, the AI agent tries to guess which image the player has selected. It has two tries to get it right.

The application is a pure web application leveraging WebAssembly. To use it, you only need a modern web browser. Yet, it has been designed for large display and has not been tested on mobiles or tablets.

The application is composed of two main parts:
1. Configure
1. Play

### Configure

Before playing a visual guessing game, you have to configure it.

Once you've logged in, you can select *Configure* to navigate to the Configure part of the application. Here, you can go through the three steps to configure a game:
- **Generate:** You write prompts and ask *Amazon Titan Image Generator** to generate images that you add to the game.
- **Prepare:** You ask *Anthropic Claude 3 Sonnet** to provide description of each image you have added to a game. You can edit the description. Once you are satistified with the descriptions, you can index them in the LanceDB vector database.
- **Test game:** You can test a game to ensure players will have a great experience. You select an image, write a description and check if the AI agent is able to retrieve the image.

Behind the scenes, the application turns descriptions into embeddings thanks to *Cohere Embed Multilingual* to index them in the vector database. It does the same when to query the vector database with your descriptions to retrieve the top-2 closest indexed description. They will serve as AI agent's guesses.

### Play

To access the Play part, you also have to first log in. Once done, you can select *Play* to start a game. You then go through four different screens:
1. **Game selection:** You select one game among the different configured games.
1. **Pick image:** You pick one image among the set of images of the game.
1. **Describe:** You provide a textual description of the image you've selected.
1. **AI guessing:** The AI agent tries to guess your image. If it doesn't get it right on the first try, you can give it a second chance or restart the game. If on the second try, it is still wrong, you can provide a different description or restart a game.

### Authentication

The application uses Amazon Cognito as an identity provider. When you use the CDK application to deploy the infrastructure, a default DemoUser is automatically created with a pseudo-random password. You can get the generated password in the outputs of your CloudFormation stack. You can also add new users directly through the Amazon Cognito administration console.

## Prerequisites

To build and deploy this .NET application, you need to install the following prerequisites on your development machine:
- Git
- .NET 8 SDK and the dotnet CLI
- Rust and Cargo
- Node.js and NPM (for AWS CDK)
- AWS CDK CLI
- Docker

## Deployment

The application leverages three LLMs hosted on Amazon Bedrock. For now, the application is hard-coded to invoke those models in the **us-east-1** region. So, you have to first request access to those models in your AWS Account in the **us-east-1** region.
Once done, you can deploy the infrastructure using the CDK CLI in any region that supports all the services used.

### Large Language Model access

You need to request access to the following models in *us-east-1* region:
- Amazon Titan Image Generator
- Anthropic Claude 3 Sonnet
- Cohere Embed Multilingual

The procedure is described [here](https://aws.amazon.com).

### CDK deployment

To deploy the infrastructure, open a terminal and run the following commands:

```bash
git clone https://github.com/build-on-aws/dotnet-visual-guessing-game
cd dotnet-visual-guessing-game/src
cdk bootstrap
cdk deploy
```

Once the CDK app is deployed, write down the following outputs:
- **WebClientUrl:** Use this URL to navigate to the application
- **DemoUserPassword:** Write down this password. You won't be able to reset the password as no email address is linked to the user. You can also get the password from the outputs tab of the CloudFormation stack in the CloudFormation console.

To configure the backend API, you also need to write down the following outputs that are described [here](#configuring-the-backend-api-appsettings-to-run-locally):
- **Authority**
- **ClientId**
- **CognitoDomainName**
- **CognitoRegion**
- **IdentityPoolId**
- **ImageStorageBucketName**
- **LanceDBIndexFunction**
- **LanceDBQueryFunction**

If you didn't write them down, you can find them in the outputs of the CloudFormation stack in the CloudFormation console.

**Note** If you've missed to write down the password, you cand find it back in the output tab of the CloudFormation stack in the CloudFormation console.

## Build locally

You can easily build and test the frontend web application and the backend API locally. Yet, you need to deploy the infrastructure at least once.

The frontend web application requires the following components to be deployed to work locally:
- Amazon Cognito User Pool for user authentication

The backend API requires the following components to be deployed to work locally:
- Amazon Cognito Identity Provider for user-based temporary credentials for accessing AWS services
- Amazon S3 bucket to store the generated images
- AWS Lambda for running the LanceDB vector database engine
- Amazon S3 bucket for LanceDB vector database storage
- Amazon Bedrock model accesses

### Configuring the backend API appsettings to run locally

Once your infrastructure is deployed, you need to configure the backend API appsettings.Development.json file. The file is stored in the following folder:

```
src/VisualGuessingGame.API
```

Here are the properties you need to set:
- **Authority:** the authority url of your Cognito User Pool in the following format *https://cognito-idp.\{REGION}.amazonaws.com/\{COGNITO_USER__POOL_ID}*,
- **ClientId:** The client id of your application declared in your Cognito User Pool
- **CognitoDomainName:** the domain name of your Cognito User Pool
- **CognitoRegion:** the region where your Cognito User Pool in the format *eu-west-1*
- **IdentityPoolId:** the Id of your Cognito Identity Pool
- **ImageStorageBucketName:** the name of the Amazon S3 bucket used to store the images
- **LanceDBIndexFunction:** the ARN of the Lambda function executing LanceDB indexation request
- **LanceDBQueryFunction:** the ARN of the Lambda function executing LanceDB query request

## Run the backend API locally

Open a terminal and set your current directory to:

```
src/VisualGuessingGame.API
```

Run the following command:

```bash
dotnet run .\VisualGuessingGame.API.csproj
```

## Run the frontend web application locally

```
src/VisualGuessingGame.Client
```

Run the following command:

```bash
dotnet run .\VisualGuessingGame.Client.csproj
```

## Navigate to the application locally

Open a browser and navigate to the following URL:

```
https://localhost:7215
```


## Architecture

## Contributions

See [CONTRIBUTING](CONTRIBUTING.md) for more information.

## Security

See [CONTRIBUTING](CONTRIBUTING.md#security-issue-notifications) for more information.

## License

This project is licensed under the MIT-0 License. See the LICENSE file.

