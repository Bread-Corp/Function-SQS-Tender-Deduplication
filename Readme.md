# 🔄 SQS Tender Deduplication AWS Lambda

[![AWS Lambda](https://img.shields.io/badge/AWS-Lambda-orange.svg)](https://aws.amazon.com/lambda/)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![Amazon SQS](https://img.shields.io/badge/AWS-SQS-yellow.svg)](https://aws.amazon.com/sqs/)
[![Amazon RDS](https://img.shields.io/badge/AWS-RDS-9d68c4.svg)](https://aws.amazon.com/rds/)
[![SQL Server](https://img.shields.io/badge/SQL%20Server-CC2727.svg)](https://www.microsoft.com/sql-server/)

**The guardian of the tender pipeline!** 🛡️ This repository contains the source code for the SQS Tender Deduplication Lambda, a critical component of the tender data processing pipeline. Its mission is to efficiently filter out duplicate and expired tender messages received from various scrapers, ensuring that only unique, valid tenders are passed to downstream services for AI enrichment.

## 📚 Table of Contents

- [🎯 Overview](#-overview)
- [🏗️ Architecture](#️-architecture)
- [✨ Core Features](#-core-features)
- [🚀 Getting Started](#-getting-started)
- [⚙️ Configuration](#️-configuration)
- [📦 Deployment Guide](#-deployment-guide)
- [🧰 Troubleshooting Guide](#-troubleshooting-guide)
- [🤝 Contributing](#-contributing)

## 🎯 Overview

The function springs into action when messages arrive in a central SQS queue (`tender-queue`). It first validates incoming messages to reject tenders that are already closed (i.e., their `closingDate` is in the past). ⏰

For valid, open tenders, it then inspects the message to extract its source and tender number, performs a lightning-fast lookup against an in-memory cache of existing tenders from the primary RDS database, and routes the message to the appropriate destination queue. 🚄

- **✅ Unique, Valid Tenders** are sent to the `AIQueue.fifo` for processing.
- **❌ Duplicate & Closed Tenders** are sent to the `DuplicateQueue.fifo`. For closed tenders, a `failureReason` is added to the message body for crystal-clear traceability.

This process prevents costly and redundant processing by the downstream AI services and ensures rock-solid data integrity. 💎

## 🏗️ Architecture

The function operates within a secure, serverless architecture inside our primary VPC like a fortress! 🏰

### 🔄 Data Flow:

1. **📥 Ingest**: Scrapers push raw tender JSON data into the `tender-queue`.
2. **⚡ Trigger**: SQS triggers the Deduplication Lambda with a batch of messages.
3. **🗄️ Cache Population**: On a cold start, the Lambda securely connects to the RDS (MS SQL Server) database via the VPC to populate its in-memory deduplication cache.
4. **🔍 Validation**: For each message, the Lambda performs a **Tender Validation Check**. It parses the `closingDate`, assumes unspecified date-times are SAST (South Africa Standard Time), converts them to UTC, and compares them against the current UTC date.
5. **🔄 Deduplication**: If the tender is valid (not closed), the Lambda performs a **Deduplication Check** against the in-memory `HashSet` using the tender's `source` and `tenderNumber`.
6. **🎯 Routing**: Based on the checks, the Lambda routes the message:
   - **✅ Unique & Valid** → `AIQueue.fifo`
   - **❌ Duplicate or Closed** → `DuplicateQueue.fifo`. If closed, a `failureReason` is added to the JSON body.
7. **✅ Acknowledge**: Finally, the Lambda deletes the processed messages from the `tender-queue`.

All communication with AWS services (SQS, RDS) is handled securely and privately via VPC Endpoints. 🔐

## ✨ Core Features

- **⏰ Tender Expiry Validation**: A smart validation service runs *before* deduplication. It intelligently parses the `closingDate`, assumes unspecified times are SAST (South Africa Standard Time) and converts to UTC, then rejects any tender that is already closed. This saves precious processing resources on expired items!

- **🚀 High-Throughput Processing**: Designed to handle thousands of messages per minute using a continuous polling strategy within the Lambda execution window.

- **⚡ Efficient Deduplication**: Utilises a static in-memory `HashSet` for near-instantaneous O(1) duplicate lookups (for valid tenders), dramatically reducing database load.

- **🎯 Optimised Database Access**: Queries the database only once per Lambda cold start to populate the cache, minimising database connections and read operations.

- **🪶 Lightweight Message Parsing**: Avoids full JSON deserialisation by only parsing the necessary `source`, `tenderNumber`, and `closingDate` fields, improving performance and reducing memory usage.

- **📋 Traceable Rejection**: Closed tenders and malformed JSON messages are routed to the `DuplicateQueue` with a `failureReason` added to the JSON body, providing crystal-clear traceability for monitoring.

- **🔒 Secure by Design**: Operates entirely within a private VPC, with no public internet access. All communication with AWS services is handled via secure VPC endpoints.

- **🛡️ Robust Error Handling**: Correctly routes malformed messages and handles failed SQS operations to prevent data loss.

## 🚀 Getting Started

Ready to dive in? Follow these steps to set up the project for local development! 🎉

### 📋 Prerequisites

- .NET 8 SDK 💻
- AWS CLI configured with appropriate credentials 🔑
- Visual Studio 2022 or VS Code with C# extensions 🛠️

### 🔧 Local Setup

1. **📁 Clone the repository:**
   ```bash
   git clone <your-repository-url>
   cd Sqs-Deduplication-Lambda
   ```

2. **📦 Restore Dependencies:**
   ```bash
   dotnet restore
   ```

3. **🔐 Configure User Secrets:**
   This project uses .NET's Secret Manager to handle the database connection string securely during local development.
   ```bash
   dotnet user-secrets init
   dotnet user-secrets set "DB_CONNECTION_STRING" "your-local-or-dev-db-connection-string"
   ```

   > **💡 Note:** For the function to access the RDS database from your local machine, your IP address may need to be whitelisted in the RDS instance's security group.

## ⚙️ Configuration

The Lambda function is configured via environment variables. These must be set in the Lambda function's configuration in AWS. 🔧

| Variable Name | Required | Description |
|---------------|----------|-------------|
| `SOURCE_QUEUE_URL` | ✅ Yes | The URL of the source SQS queue (tender-queue). |
| `AI_QUEUE_URL` | ✅ Yes | The URL of the destination FIFO queue for unique tenders. |
| `DUPLICATE_QUEUE_URL` | ✅ Yes | The URL of the destination FIFO queue for duplicate and rejected tenders. |
| `DB_CONNECTION_STRING` | ✅ Yes | The full connection string for the RDS SQL Server database. |

## 📦 Deployment

This section covers three deployment methods for the Tender Deduplication Lambda Function. Choose the method that best fits your workflow and infrastructure preferences.

### 🛠️ Prerequisites

Before deploying, ensure you have:
- AWS CLI configured with appropriate credentials 🔑
- .NET 8 SDK installed locally
- AWS SAM CLI installed (for SAM deployment)
- Access to AWS Lambda, SQS, RDS, and VPC services ☁️
- Visual Studio 2022 or VS Code with C# extensions (for AWS Toolkit deployment)

### 🎯 Method 1: AWS Toolkit Deployment

Deploy directly through Visual Studio using the AWS Toolkit extension.

#### Setup Steps:
1. **Install AWS Toolkit** for Visual Studio 2022
2. **Configure AWS Profile** with your credentials in Visual Studio
3. **Open Solution** containing `TenderDeduplication.csproj`

#### Deploy Process:
1. **Right-click** the project in Solution Explorer
2. **Select** "Publish to AWS Lambda" from the context menu
3. **Configure Lambda Settings**:
   - Function Name: `TenderDeduplicationLambda`
   - Runtime: `.NET 8`
   - Handler: `TenderDeduplication::TenderDeduplication.Function::FunctionHandler`
   - Memory: `512 MB`
   - Timeout: `300 seconds`
4. **Configure VPC Settings**:
   - VPC: Select your existing VPC
   - Security Groups: `sg-0043b58a403174a59`
   - Subnets: `subnet-0f47b68400d516b1e`, `subnet-072a27234084339fc`
5. **Set Environment Variables**:
   ```
   AI_QUEUE_URL=https://sqs.us-east-1.amazonaws.com/211635102441/AIQueue.fifo
   DB_CONNECTION_STRING=
   DUPLICATE_QUEUE_URL=https://sqs.us-east-1.amazonaws.com/211635102441/DuplicatesQueue.fifo
   SOURCE_QUEUE_URL=https://sqs.us-east-1.amazonaws.com/211635102441/TenderQueue.fifo
   ```
6. **Configure IAM Role** with required permissions for SQS, RDS, VPC, and CloudWatch
7. **Set up SQS Trigger** manually after deployment

#### Post-Deployment:
- Test the function using the AWS Toolkit test feature
- Monitor logs through CloudWatch integration
- Verify SQS trigger configuration and batch processing

### 🚀 Method 2: SAM Deployment

Use AWS SAM for infrastructure-as-code deployment with the provided template.

#### Initial Setup:
```bash
# Install AWS SAM CLI
pip install aws-sam-cli

# Install .NET 8 SDK
# Download from https://dotnet.microsoft.com/download/dotnet/8.0

# Verify installations
sam --version
dotnet --version
```

#### Build and Deploy:
```bash
# Build the .NET 8 application
dotnet build -c Release

# Build the SAM application
sam build

# Deploy with guided configuration (first time)
sam deploy --guided

# Follow the prompts:
# Stack Name: tender-deduplication-stack
# AWS Region: us-east-1 (or your preferred region)
# Confirm changes before deploy: Y
# Allow SAM to create IAM roles: Y
# Save parameters to samconfig.toml: Y
```

#### Environment Variables Setup:
The template already includes the required environment variables:

```yaml
# Already configured in TenderDeduplicationLambda.yaml
Environment:
  Variables:
    AI_QUEUE_URL: https://sqs.us-east-1.amazonaws.com/211635102441/AIQueue.fifo
    DB_CONNECTION_STRING: 
    DUPLICATE_QUEUE_URL: https://sqs.us-east-1.amazonaws.com/211635102441/DuplicatesQueue.fifo
    SOURCE_QUEUE_URL: https://sqs.us-east-1.amazonaws.com/211635102441/TenderQueue.fifo
```

#### Subsequent Deployments:
```bash
# Quick deployment after initial setup
dotnet build -c Release
sam build && sam deploy
```

#### Local Testing with SAM:
```bash
# Test function locally (requires Docker)
sam local invoke TenderDeduplicationLambda

# Start local API for testing
sam local start-api
```

#### SAM Deployment Advantages:
- ✅ Complete infrastructure management including SQS queues
- ✅ VPC and security group configuration included
- ✅ Environment variables defined in template
- ✅ IAM permissions automatically configured
- ✅ Easy rollback capabilities
- ✅ CloudFormation integration
- ✅ SQS trigger automatically configured

### 🔄 Method 3: Workflow Deployment (CI/CD)

Automated deployment using GitHub Actions workflow for production environments.

#### Setup Requirements:
1. **GitHub Repository Secrets**:
   ```
   AWS_ACCESS_KEY_ID: Your AWS access key
   AWS_SECRET_ACCESS_KEY: Your AWS secret key
   AWS_REGION: us-east-1 (or your target region)
   ```

2. **Pre-existing Lambda Function**: The workflow updates an existing function, so deploy initially using Method 1 or 2.

#### Deployment Process:
1. **Create Release Branch**:
   ```bash
   # Create and switch to release branch
   git checkout -b release
   
   # Make your changes to the .NET code
   # Commit changes
   git add .
   git commit -m "feat: update tender deduplication logic"
   
   # Push to trigger deployment
   git push origin release
   ```

2. **Automatic Deployment**: The workflow will:
   - Checkout the code
   - Set up .NET 8 SDK
   - Install AWS Lambda Tools
   - Build and package the Lambda function
   - Configure AWS credentials
   - Update the existing Lambda function code
   - Maintain existing configuration (environment variables, VPC settings, etc.)

#### Manual Trigger:
You can also trigger deployment manually:
1. Go to **Actions** tab in your GitHub repository
2. Select **"Deploy .NET Lambda to AWS"** workflow
3. Click **"Run workflow"**
4. Choose the `release` branch
5. Click **"Run workflow"** button

#### Workflow Deployment Advantages:
- ✅ Automated CI/CD pipeline
- ✅ Consistent deployment process
- ✅ Audit trail of deployments
- ✅ Easy rollback to previous commits
- ✅ No local environment dependencies
- ✅ Automatic .NET build and packaging

### 🔧 Post-Deployment Configuration

Regardless of deployment method, verify the following:

#### Environment Variables Verification:
Ensure these environment variables are properly set:

```bash
# Verify environment variables via AWS CLI
aws lambda get-function-configuration \
    --function-name TenderDeduplicationLambda \
    --query 'Environment.Variables'
```

Expected output:
```json
{
    "AI_QUEUE_URL": "https://sqs.us-east-1.amazonaws.com/211635102441/AIQueue.fifo",
    "DB_CONNECTION_STRING": "",
    "DUPLICATE_QUEUE_URL": "https://sqs.us-east-1.amazonaws.com/211635102441/DuplicatesQueue.fifo",
    "SOURCE_QUEUE_URL": "https://sqs.us-east-1.amazonaws.com/211635102441/TenderQueue.fifo"
}
```

#### VPC Configuration Verification:
Verify VPC settings for database and SQS access:

```bash
# Check VPC configuration
aws lambda get-function-configuration \
    --function-name TenderDeduplicationLambda \
    --query 'VpcConfig'
```

#### SQS Trigger Configuration:
Ensure the SQS trigger is properly configured:

```bash
# List event source mappings
aws lambda list-event-source-mappings \
    --function-name TenderDeduplicationLambda

# Verify batch size and queue configuration
aws lambda get-event-source-mapping \
    --uuid [event-source-mapping-uuid]
```

#### Database Access Verification:
Test database connectivity from the Lambda function:

```bash
# Invoke function to test database connection
aws lambda invoke \
    --function-name TenderDeduplicationLambda \
    --payload '{"Records":[]}' \
    response.json
```

### 🧪 Testing Your Deployment

After deployment, test the function thoroughly:

#### Test Message Processing:
```bash
# Send test message to source queue
aws sqs send-message \
    --queue-url https://sqs.us-east-1.amazonaws.com/211635102441/TenderQueue.fifo \
    --message-body '{"source":"TestSource","tenderNumber":"TEST-001","closingDate":"2025-12-31T23:59:59"}' \
    --message-group-id "TestGroup" \
    --message-deduplication-id "test-$(date +%s)"

# Monitor function execution
aws logs tail /aws/lambda/TenderDeduplicationLambda --follow
```

#### Expected Success Indicators:
- ✅ Function executes without errors
- ✅ CloudWatch logs show successful database connection
- ✅ Messages are properly routed to AI queue or duplicate queue
- ✅ No timeout or memory errors
- ✅ Proper deduplication logic working
- ✅ SQS batch processing functioning correctly

### 🔍 Monitoring and Maintenance

#### CloudWatch Metrics to Monitor:
- **Duration**: Function execution time for batch processing
- **Error Rate**: Failed deduplication operations
- **Memory Utilization**: RAM usage during processing
- **SQS Metrics**: Message processing rates and dead letter queues
- **Database Connection Health**: RDS connection metrics

#### Log Analysis:
```bash
# View recent logs
aws logs tail /aws/lambda/TenderDeduplicationLambda --follow

# Search for deduplication statistics
aws logs filter-log-events \
    --log-group-name /aws/lambda/TenderDeduplicationLambda \
    --filter-pattern "Processed batch"

# Search for database connection issues
aws logs filter-log-events \
    --log-group-name /aws/lambda/TenderDeduplicationLambda \
    --filter-pattern "Database connection"

# Monitor SQS routing decisions
aws logs filter-log-events \
    --log-group-name /aws/lambda/TenderDeduplicationLambda \
    --filter-pattern "Routed to"
```

### 🚨 Troubleshooting Deployments

<details>
<summary><strong>.NET 8 Runtime Issues</strong></summary>

**Issue**: Function fails to start or throws runtime errors

**Solution**: Ensure proper .NET 8 configuration:
- Verify the handler path: `TenderDeduplication::TenderDeduplication.Function::FunctionHandler`
- Check that all NuGet packages are compatible with .NET 8
- Ensure the project targets `net8.0` framework
- Verify all dependencies are included in the deployment package
</details>

<details>
<summary><strong>Database Connection Failures</strong></summary>

**Issue**: Cannot connect to RDS SQL Server from Lambda

**Solution**: Verify VPC and security configuration:
- Ensure Lambda is in the same VPC as RDS
- Check security groups allow traffic on port 1433
- Verify RDS is accessible from Lambda subnets
- Test connection string format and credentials
- Check if RDS is in a maintenance window
</details>

<details>
<summary><strong>SQS Message Processing Issues</strong></summary>

**Issue**: Messages not being processed or routed incorrectly

**Solution**: Debug SQS configuration:
- Verify SQS trigger is configured with correct batch size (10)
- Check message format matches expected JSON structure
- Ensure FIFO queue attributes are properly set
- Verify message group ID and deduplication ID logic
- Monitor dead letter queue for failed messages
</details>

<details>
<summary><strong>VPC Networking Problems</strong></summary>

**Issue**: Function times out or cannot access AWS services

**Solution**: Check VPC configuration:
- Ensure VPC endpoints exist for SQS service access
- Verify route tables and NAT gateway configuration
- Check that subnets have proper CIDR ranges
- Ensure DNS resolution is enabled in VPC settings
- Verify security group rules allow outbound HTTPS traffic
</details>

<details>
<summary><strong>Memory and Performance Issues</strong></summary>

**Issue**: Function runs out of memory or times out

**Solution**: Optimize function performance:
- Increase memory allocation (current: 512 MB)
- Optimize batch processing logic
- Review database query performance
- Consider implementing connection pooling
- Monitor cold start times and optimize accordingly
</details>

<details>
<summary><strong>Environment Variables Missing</strong></summary>

**Issue**: Function cannot access required configuration

**Solution**: Set environment variables using AWS CLI:
```bash
aws lambda update-function-configuration \
    --function-name TenderDeduplicationLambda \
    --environment Variables='{
        "AI_QUEUE_URL":"https://sqs.us-east-1.amazonaws.com/211635102441/AIQueue.fifo",
        "DB_CONNECTION_STRING":"",
        "DUPLICATE_QUEUE_URL":"https://sqs.us-east-1.amazonaws.com/211635102441/DuplicatesQueue.fifo",
        "SOURCE_QUEUE_URL":"https://sqs.us-east-1.amazonaws.com/211635102441/TenderQueue.fifo"
    }'
```
</details>

<details>
<summary><strong>Workflow Deployment Fails</strong></summary>

**Issue**: GitHub Actions workflow errors

**Solution**: 
- Check repository secrets are correctly configured
- Verify .NET 8 SDK is properly installed in workflow
- Ensure AWS Lambda Tools installation succeeds
- Check that TenderDeduplication.csproj exists in repository
- Verify target Lambda function exists in AWS
</details>

Choose the deployment method that best fits your development workflow and infrastructure requirements. SAM deployment is recommended for development environments, while workflow deployment excels for production systems requiring automated CI/CD pipelines.

## 🧰 Troubleshooting Guide

Don't panic! This section documents common issues encountered during deployment and how to solve them. 🔧

<details>
<summary><strong>🚨 Error: Connection Timed Out or Function Hangs When Sending SQS Messages</strong></summary>

This is a complex VPC networking issue with several potential causes. Follow this checklist in order:

1. **⏱️ Lambda Timeout Setting**: The default Lambda timeout is 30 seconds. This is often too short for a function performing network I/O.
   - **🔧 Fix**: In the Lambda's Configuration > General configuration, increase the timeout to at least 3 minutes.

2. **🚫 Missing VPC Endpoint**: A Lambda in a private VPC cannot access public AWS service endpoints.
   - **🔧 Fix**: Create a VPC Interface Endpoint for SQS (`com.amazonaws.region.sqs`) and place it in the same private subnets as your Lambda.

3. **🌐 VPC DNS Settings**: The VPC must be configured to use the Amazon DNS server to resolve the endpoint's private name.
   - **🔧 Fix**: In the VPC Dashboard, select your VPC, click Actions > Edit VPC settings, and ensure both "Enable DNS resolution" and "Enable DNS hostnames" are checked.

4. **🔒 Endpoint Security Group**: The endpoint's own security group must allow inbound traffic from the Lambda.
   - **🔧 Fix**: Edit the inbound rules on the security group attached to the VPC Endpoint. Add a rule allowing HTTPS (Port 443) from the source security group ID of your Lambda function. This is the most commonly missed step!

</details>

<details>
<summary><strong>⚠️ Error: DbContext Concurrency Exception (A second operation was started...)</strong></summary>

**🔍 Cause**: The DbContext was registered as a singleton, but multiple parallel queries (`Task.WhenAll`) were attempting to use it simultaneously. DbContext is not thread-safe.

**✅ Solution**: Change the dependency injection registration in `Function.cs` from `services.AddDbContext(...)` to `services.AddDbContextFactory(...)`. Inject the `IDbContextFactory` into the service and create a new DbContext instance for each database operation.

</details>

<details>
<summary><strong>🚫 Error: Function Not Triggered by SQS After VPC Placement</strong></summary>

**🔍 Cause**: The SQS trigger's permissions can become invalid after a Lambda is moved into a VPC.

**✅ Solution**: In the Lambda's Configuration > Triggers tab, delete the existing SQS trigger and immediately re-add it. This forces AWS to regenerate the correct resource-based invocation policy.

</details>

<details>
<summary><strong>📨 Error: Sending to .fifo Queues Fails or Times Out</strong></summary>

**🔍 Cause**: SQS FIFO queues require two mandatory attributes for every message: `MessageGroupId` and `MessageDeduplicationId`.

**✅ Solution**: The `SqsService` was updated to detect if a queue is FIFO. It now generates a unique `MessageDeduplicationId` (`Guid.NewGuid()`) and uses the tender source as the `MessageGroupId`.

</details>

<details>
<summary><strong>📅 Error: Date Validation Issues with SAST Time Zone</strong></summary>

**🔍 Cause**: The tender validation may incorrectly parse closing dates or fail to properly convert SAST to UTC.

**✅ Solution**: Ensure the validation service correctly handles date parsing:
- Unspecified date-times are assumed to be SAST (UTC+2) 🌍
- All comparisons are done in UTC ⏰
- Check that the `TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time")` is available in the Lambda runtime 🔍

</details>

## 🤝 Contributing

We welcome contributions with open arms! Please follow these steps: 🎉

1. Create a new feature branch from `main` (e.g., `feature/add-new-source`). 🌿
2. Make your changes. ✏️
3. Commit your work and push it to the remote repository. 📤
4. Open a Pull Request for review. 👀

---

> Built with love, bread, and code by **Bread Corporation** 🦆❤️💻
