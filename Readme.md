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

## 📦 Deployment Guide

Time to ship it! Follow these steps to package and deploy the function to AWS Lambda. 🚢

### 🔨 Step 1: Create the Deployment Package

Run the following command from the project's root directory. This will build the project in Release mode and create a `.zip` file ready for deployment.

```bash
dotnet lambda package -c Release -o ./build/deploy-package.zip
```

### 🌐 Step 2: Deploy to AWS Lambda

1. Navigate to the AWS Lambda console and select the `TenderDeduplication` function. 🎛️
2. Under the "Code source" section, click the "Upload from" button. 📤
3. Select ".zip file". 📁
4. Upload the `deploy-package.zip` file located in the `build` directory. ⬆️
5. Click Save. 💾

> 🚨 **Important:** Ensure all AWS prerequisites (IAM roles, VPC settings, etc.) are in place before deploying.

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
