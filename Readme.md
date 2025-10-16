# SQS Tender Deduplication AWS Lambda

This repository contains the source code for the SQS Tender Deduplication Lambda, a critical component of the tender data processing pipeline. Its primary function is to efficiently filter out duplicate tender messages received from various scrapers, ensuring that only unique tenders are passed to downstream services for AI enrichment.

## Table of Contents

- [Overview](#1-overview)
- [Architecture](#2-architecture)
- [Core Features](#3-core-features)
- [Getting Started](#4-getting-started)
- [Configuration](#5-configuration)
- [Deployment Guide](#6-deployment-guide)
- [Troubleshooting Guide](#7-troubleshooting-guide)
- [Contributing](#8-contributing)

## 1. Overview

The function is triggered by messages arriving in a central SQS queue (`tender-queue`). It inspects each message to extract its source and tender number, performs a high-speed lookup against an in-memory cache of existing tenders from the primary RDS database, and then routes the message to the appropriate destination queue.

- **Unique Tenders** are sent to the `AIQueue.fifo` for processing.
- **Duplicate Tenders** are sent to the `DuplicateQueue.fifo` for logging and monitoring.

This process prevents costly and redundant processing by the downstream AI services and ensures data integrity.

## 2. Architecture

The function operates within a secure, serverless architecture inside our primary VPC.

### Data Flow:

1. Scrapers push raw tender JSON data into the `tender-queue`.
2. SQS triggers the Deduplication Lambda with a batch of messages.
3. The Lambda securely connects to the RDS (MS SQL Server) database via the VPC to populate its in-memory cache on a cold start.
4. The Lambda uses a VPC Interface Endpoint to privately and securely communicate with the SQS API.
5. Based on the deduplication check, the Lambda sends messages to either the `AIQueue.fifo` or the `DuplicateQueue.fifo`.
6. Finally, the Lambda deletes the processed messages from the `tender-queue`.

## 3. Core Features

- **High-Throughput Processing**: Designed to handle thousands of messages per minute using a continuous polling strategy within the Lambda execution window.

- **Efficient Deduplication**: Utilises a static in-memory HashSet for near-instantaneous O(1) duplicate lookups, dramatically reducing database load.

- **Optimised Database Access**: Queries the database only once per Lambda cold start to populate the cache, minimising database connections and read operations.

- **Lightweight Message Parsing**: Avoids full JSON deserialisation by only parsing the necessary `source` and `tenderNumber` fields, improving performance and reducing memory usage.

- **Secure by Design**: Operates entirely within a private VPC, with no public internet access. All communication with AWS services is handled via secure VPC endpoints.

- **Robust Error Handling**: Correctly routes malformed messages and handles failed SQS operations to prevent data loss.

## 4. Getting Started

Follow these steps to set up the project for local development.

### Prerequisites

- .NET 8 SDK
- AWS CLI configured with appropriate credentials.
- Visual Studio 2022 or VS Code with C# extensions.

### Local Setup

1. **Clone the repository:**
   ```bash
   git clone <your-repository-url>
   cd Sqs-Deduplication-Lambda
   ```

2. **Restore Dependencies:**
   ```bash
   dotnet restore
   ```

3. **Configure User Secrets:**
   This project uses .NET's Secret Manager to handle the database connection string securely during local development.
   ```bash
   dotnet user-secrets init
   dotnet user-secrets set "DB_CONNECTION_STRING" "your-local-or-dev-db-connection-string"
   ```

   > **Note:** For the function to access the RDS database from your local machine, your IP address may need to be whitelisted in the RDS instance's security group.

## 5. Configuration

The Lambda function is configured via environment variables. These must be set in the Lambda function's configuration in AWS.

| Variable Name | Required | Description |
|---------------|----------|-------------|
| `SOURCE_QUEUE_URL` | Yes | The URL of the source SQS queue (tender-queue). |
| `AI_QUEUE_URL` | Yes | The URL of the destination FIFO queue for unique tenders. |
| `DUPLICATE_QUEUE_URL` | Yes | The URL of the destination FIFO queue for duplicate tenders. |
| `DB_CONNECTION_STRING` | Yes | The full connection string for the RDS SQL Server database. |

## 6. Deployment Guide

Follow these steps to package and deploy the function to AWS Lambda.

### Step 1: Create the Deployment Package

Run the following command from the project's root directory. This will build the project in Release mode and create a `.zip` file ready for deployment.

```bash
dotnet lambda package -c Release -o ./build/deploy-package.zip
```

### Step 2: Deploy to AWS Lambda

1. Navigate to the AWS Lambda console and select the `TenderDeduplication` function.
2. Under the "Code source" section, click the "Upload from" button.
3. Select ".zip file".
4. Upload the `deploy-package.zip` file located in the `build` directory.
5. Click Save.

> Ensure all AWS prerequisites (IAM roles, VPC settings, etc.) are in place before deploying.

## 7. Troubleshooting Guide

This section documents common issues encountered during deployment and how to solve them.

<details>
<summary><strong>Error: Connection Timed Out or Function Hangs When Sending SQS Messages</strong></summary>

This is a complex VPC networking issue with several potential causes. Follow this checklist in order:

1. **Lambda Timeout Setting**: The default Lambda timeout is 30 seconds. This is often too short for a function performing network I/O.
   - **Fix**: In the Lambda's Configuration > General configuration, increase the timeout to at least 3 minutes.

2. **Missing VPC Endpoint**: A Lambda in a private VPC cannot access public AWS service endpoints.
   - **Fix**: Create a VPC Interface Endpoint for SQS (`com.amazonaws.region.sqs`) and place it in the same private subnets as your Lambda.

3. **VPC DNS Settings**: The VPC must be configured to use the Amazon DNS server to resolve the endpoint's private name.
   - **Fix**: In the VPC Dashboard, select your VPC, click Actions > Edit VPC settings, and ensure both "Enable DNS resolution" and "Enable DNS hostnames" are checked.

4. **Endpoint Security Group**: The endpoint's own security group must allow inbound traffic from the Lambda.
   - **Fix**: Edit the inbound rules on the security group attached to the VPC Endpoint. Add a rule allowing HTTPS (Port 443) from the source security group ID of your Lambda function. This is the most commonly missed step.

</details>

<details>
<summary><strong>Error: DbContext Concurrency Exception (A second operation was started...)</strong></summary>

**Cause**: The DbContext was registered as a singleton, but multiple parallel queries (`Task.WhenAll`) were attempting to use it simultaneously. DbContext is not thread-safe.

**Solution**: Change the dependency injection registration in `Function.cs` from `services.AddDbContext(...)` to `services.AddDbContextFactory(...)`. Inject the `IDbContextFactory` into the service and create a new DbContext instance for each database operation.

</details>

<details>
<summary><strong>Error: Function Not Triggered by SQS After VPC Placement</strong></summary>

**Cause**: The SQS trigger's permissions can become invalid after a Lambda is moved into a VPC.

**Solution**: In the Lambda's Configuration > Triggers tab, delete the existing SQS trigger and immediately re-add it. This forces AWS to regenerate the correct resource-based invocation policy.

</details>

<details>
<summary><strong>Error: Sending to .fifo Queues Fails or Times Out</strong></summary>

**Cause**: SQS FIFO queues require two mandatory attributes for every message: `MessageGroupId` and `MessageDeduplicationId`.

**Solution**: The `SqsService` was updated to detect if a queue is FIFO. It now generates a unique `MessageDeduplicationId` (`Guid.NewGuid()`) and uses the tender source as the `MessageGroupId`.

</details>

## 8. Contributing

We welcome contributions! Please follow these steps:

1. Create a new feature branch from `main` (e.g., `feature/add-new-source`).
2. Make your changes.
3. Commit your work and push it to the remote repository.
4. Open a Pull Request for review.
