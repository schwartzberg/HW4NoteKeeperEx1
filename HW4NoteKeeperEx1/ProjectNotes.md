# Project Notes

## 1. Homework Assignment Title and Number
**Assignment #4 - Note Keeper**

---

## 2. Student Information
**Name:** Paul Schwartzberg  
**Email:** paulschwartzberg@outlook.com

---

## 3. Attribution Regarding the Use of AI


### What I Used AI For (Initial Guidance and Debugging Only):

 
### What I Coded by Hand and/or Modified Considerably:

 
### Summary
 
---

## 4. Notes for the TA

**No special installation or setup is required.** The application should run directly after:
1. Setting the Azure OpenAI configuration values in App Service Environment Variables (as documented below)
2. Restoring NuGet packages
3. Building the solution

The application uses standard ASP.NET Core 10.0 features and does not require any additional tools or dependencies
beyond what is included in the `.csproj` file.

---

## 4.1. Homework 3 Core Implementation

**HOMEWORK 4 CORE FEATURE:**

**Azure Blob Storage for Note Attachments**

The core feature for HW4 is the implementation of Azure Blob Storage for storing note attachments:

- **Architecture:** Each note has its own private blob container named with the note's GUID ID (lowercase)
- **Attachment Endpoints:**
  - `PUT /notes/{noteId}/attachments/{attachmentId}` - Upload or update an attachment
    - Returns 201 (Created) with Location header for new attachments
    - Returns 204 (No Content) for updates to existing attachments
    - Returns 400 (Bad Request) for invalid GUID format
    - Returns 403 (Forbidden) when MaxAttachments limit is reached (for new uploads only)
    - **Note:** Does NOT check database - works purely with blob storage
  - `GET /notes/{noteId}/attachments/{attachmentId}` - Retrieve a single attachment
    - Returns 200 (OK) with file stream and Content-Disposition header
    - Returns 400 (Bad Request) for invalid GUID format
    - Returns 404 (Not Found) if attachment or container doesn't exist
  - `GET /notes/{noteId}/attachments` - Retrieve all attachment metadata for a note
    - Returns 200 (OK) with array of attachment info (empty array if no attachments)
    - Returns 400 (Bad Request) for invalid GUID format
    - Returns 404 (Not Found) if container doesn't exist
    - **Note:** 404 behavior goes beyond spec requirement (spec only requires 200 OK) for consistency with single GET endpoint
  - `DELETE /notes/{noteId}/attachments/{attachmentId}` - Delete an attachment
    - Returns 204 (No Content) on successful deletion or if attachment doesn't exist (idempotent)
    - Returns 400 (Bad Request) for invalid GUID format
    - Returns 500 (Internal Server Error) if deletion fails
    - **Note:** Does NOT check database - works purely with blob storage

- **MaxAttachments Limit:** Configurable maximum number of attachments per note (default: 3)
  - Configuration: `NoteLimits:MaxAttachments` in appsettings.json or `NoteLimits__MaxAttachments` in Azure environment variables
  - Limit is enforced only for **new** uploads; updating existing attachments bypasses the limit check
  
- **Managed Identity Authentication:** Uses `DefaultAzureCredential` for passwordless blob storage access
  - Local Development: Uses Azure CLI, Visual Studio, or VS Code credentials
  - Azure Deployment: Uses App Service's managed identity
  
- **Automatic Seeding:** `AzureStorageInitializer` seeds blob storage at startup
  - Creates containers for seeded notes if they don't exist
  - Uploads default attachment files for each seeded note
  - Idempotent: safe to run multiple times
  - Retry logic handles Azure's "ContainerBeingDeleted" transient errors (up to 10 retries with 4-second delays)

- **Seeded Attachments:**
  - "Running grocery list" → MilkAndEggs.png, Oranges.png
  - "Gift supplies notes" → WrappingPaper.png, Tape.png
  - "Valentine's Day gift ideas" → Chocolate.png, Diamonds.png, NewCar.png
  - "Azure tips" → AzureLogo.png, AzureTipsAndTricks.pdf

- **Implementation Files:**
- `Services/AzureStorageService.cs` - Blob operations (upload, delete, download, list, count, exists)
- `Controllers/NoteKeeperAttachmentController.cs` - REST API endpoints (PUT, GET single, GET all, DELETE)
- `RequestAndResultObjects/AttachmentInfoResult.cs` - Result class for GET all attachments
- `Data/AzureStorageInitializer.cs` - Startup seeding logic
- `CustomSettings/NoteLimits.cs` - MaxAttachments property added

---

## 4.2. Extra Credit and Graduate Credit Implementations



**HOMEWORK 4 EXTRA CREDIT:**

### Extra Credit 3: Managed Identities for Azure Storage Queues in Azure Function

**Requirement:** Use managed identities (instead of connection strings/keys) for authentication to Azure Storage Queues in the Azure Function.

**Implementation:**

The `AttachmentZipFunction` (and `AttachmentZipProcessor`) authenticate to all Azure Storage resources using `DefaultAzureCredential` — no storage account keys or connection strings are stored anywhere in the code or configuration.

**Queue trigger (passwordless):**
- The queue trigger binding uses `AttachmentZipRequests__queueServiceUri` instead of a connection string, pointing to `https://st4hw3.queue.core.windows.net`
- Azure Functions resolves this as a managed identity connection because the `__queueServiceUri` suffix (no `__AccountKey`) signals credential-based auth
 
**Blob storage (passwordless):**
- `BlobStorageHelper` is constructed with `new BlobServiceClient(uri, new DefaultAzureCredential())`
- `AzureWebJobsStorage__serviceUri` replaces the traditional `AzureWebJobsStorage` connection string

**Azure infrastructure:**
- Managed Identity: `id-dbadmin` (user-assigned) is assigned to `func-HW4`
- Role assignments on `st4hw3`: `Storage Blob Data Contributor` and `Storage Queue Data Contributor`
- App setting `AttachmentZipRequests__clientId` = the client ID of `id-dbadmin`

**Local development note:**
- `local.settings.json` uses the same URI-based settings; `DefaultAzureCredential` falls back to the Azure CLI credential (`az login` as `paulschwartzberg@outlook.com`) for local testing

---

**HOMEWORK 3 EXTRA CREDIT:**

### Extra Credit Option 1: Custom Application Insights Telemetry for Attachments

**Implementation:** Custom telemetry tracking for attachment operations using Azure Application Insights.

**Events Tracked:**

1. **AttachmentCreated Event**
   - Triggered when a new attachment is uploaded (HTTP PUT creates a new blob)
   - Properties: `attachmentid` (string) - The blob ID/filename
   - Metrics: `AttachmentSize` (double) - Size in bytes
   - Implementation: `NoteKeeperAttachmentController.PutAttachment()`

2. **AttachmentUpdated Event**
   - Triggered when an existing attachment is overwritten (HTTP PUT updates existing blob)
   - Properties: `attachmentid` (string) - The blob ID/filename
   - Metrics: `AttachmentSize` (double) - Size in bytes
   - Implementation: `NoteKeeperAttachmentController.PutAttachment()`

3. **Validation Error Tracking (All Attachment Methods)**
   - Method: `TrackTrace` with `SeverityLevel.Warning`
   - Properties:
     - `ValidationError` (string) - Description of the validation error
     - `InputPayload` (string) - JSON-serialized input parameters
   - Tracked Errors:
     - Invalid noteId format (not a valid GUID)
     - Null or empty attachmentId
     - Null or empty file data
   - Applied to: PUT, DELETE, GET (single), GET (all attachments)

4. **Exception Tracking (All Attachment Methods)**
   - Method: `TrackException`
   - Properties:
     - `ExceptionMessage` (string) - The exception message
     - `InputPayload` (string) - JSON-serialized input parameters
   - Applied to all attachment operations with try-catch blocks

**Viewing Telemetry in Azure Portal:**

*Custom Events:*
```kusto
customEvents
| where name == "AttachmentCreated" or name == "AttachmentUpdated"
| project timestamp, name, customDimensions.attachmentid, customMeasurements.AttachmentSize
```

*Validation Errors:*
```kusto
traces
| where message contains "Validation Error"
| project timestamp, message, customDimensions.ValidationError, customDimensions.InputPayload
```

*Exceptions:*
```kusto
exceptions
| where customDimensions has "InputPayload"
| project timestamp, type, outerMessage, customDimensions.ExceptionMessage, customDimensions.InputPayload
```

**HOMEWORK 3 GRADUATE CREDIT:**

*No additional graduate credit implemented for HW4.*

**CARRIED OVER FROM HOMEWORK 2:**

*These components were implemented for HW2 extra/graduate credit and remain in the solution:*

1. **Managed Identity / Passwordless Database Access (Section 11)** - *Originally implemented for HW2 extra credit*
   - Microsoft Entra ID authentication for Azure SQL Database
   - Uses `Authentication=Active Directory Default` in connection string
   - No credentials stored in configuration files
   - Configuration file: `appsettings.managedidentities.json`

2. **Application Insights Telemetry (Section 10)** - *Originally implemented for HW2 graduate credit*
   - Full telemetry implementation with TrackTrace, TrackException, and TrackEvent
   - Validation errors logged with `TrackTrace` (SeverityLevel.Warning)
   - All exceptions logged with `TrackException` including InputPayload
   - Custom events tracked for successful operations
   - Live Metrics enabled with SDK Control Channel authentication
   - Instance: `appi-notekeeper-cscie94-ps-HW4`

3. **Entity Framework Core with Azure SQL Database** - *Originally implemented for HW2*
   - Code-first approach with migrations
   - Azure SQL Database: `sqldb-cscie94-2026_hw4`
   - Note and Tag models with one-to-many relationship
   - Database seeding with default notes

4. **MaxNotes Limit Feature** - *Originally implemented for HW2*
   - Configurable maximum number of notes (default: 10)
   - Returns 403 Forbidden when limit reached
   - Configuration: `NoteLimits:MaxNotes` in appsettings.json

**CARRIED OVER FROM HOMEWORK 1:**

*These components were implemented for HW1 extra/graduate credit and remain in the solution:*

1. **Blazor UI (HW4NoteKeeperEx1.BlazorUI project)** - *Originally implemented for HW1 graduate credit*
   - Left unchanged from HW1
   - Not modified for HW2 or HW4 as it was not required
   - Runs locally only; API is on Azure (Section 7)

2. **Unit/E2E Testing (HW4NoteKeeperEx1.Tests project)** - *Originally implemented for HW1 extra credit*
- Modified extensively for HW2 and HW4 to test new features
- Used for personal verification that requirements work correctly
- 27 E2E tests for attachment endpoints (NoteKeeperAttachmentE2ETests): PUT, GET single, GET all, DELETE, seeding
- 22 E2E tests for note endpoints (NoteKeeperControllerE2ETests)
- All tests run against production Azure SQL Database and Azure Blob Storage

---

## 5. Microsoft Foundry and Project Name

**Microsoft Foundry Name:** `ai-csscie94-foundry`  
**Foundry Project Name:** `note-keeper`

**Azure OpenAI Endpoint:** `https://ai-csscie94-foundry.openai.azure.com/`

---

## 6. Custom Azure Resource Abbreviations

**No custom Azure resource abbreviations were used in this project.**

All resources follow standard Azure naming conventions.

---

## 7. Azure App Service Website URL

**Production URL:**   
https://app-notekeeper-cscie94-ps-HW4-1-gjegduaqfccbd2bt.swedencentral-01.azurewebsites.net

**Note:** The Swagger UI is configured to load at the root path (`/`), so navigating to the base URL will display the interactive
API documentation.

---

## 8. Target URI for Azure OpenAI Service

**Azure OpenAI Service Endpoint:**  
`https://ai-csscie94-foundry.openai.azure.com/`

**Deployment Model Name:** `gpt-5-mini`

**Configuration Note:**  
The application expects the following environment variables to be set in Azure App Service:
- `AzureOpenAI__DeploymentUri` = `https://ai-csscie94-foundry.openai.azure.com/`
- `AzureOpenAI__ApiKey` = `[API Key - not included per requirement #9]`
- `AzureOpenAI__DeploymentModelName` = `gpt-5-mini`
- `AzureOpenAI__Temperature` = `1.0`
- `AzureOpenAI__TopP` = `1.0`
- `AzureOpenAI__MaxOutputTokens` = `500`
- `NoteLimits__MaxNotes` = `10` (or your desired limit)
- `ApplicationInsights__AuthenticationApiKey` = `fkobfb4qcixknvrhfiu47glh4uhzrajrg7hwwywg` (SDK Control Channel authentication key for Live Metrics)
- `NoteLimits__MaxAttachments` = `3` (or your desired limit)
- `StorageAccountSettings__Url` = `https://<your-storage-account>.blob.core.windows.net/`
- `StorageAccountSettings__TenantId` = `<your-tenant-id>` (for local development only)
- `StorageAccountSettings__AccountName` = `<your-storage-account-name>`

### How to Configure NoteLimits in Azure App Service

1. **Via Azure Portal:**
   - Navigate to your App Service: `app-notekeeper-cscie94-ps-HW4-1-gjegduaqfccbd2bt`
   - Go to **Settings** → **Environment variables**
   - Click **+ Add** to add new application settings:
     - `NoteLimits__MaxNotes` = `10` (or your desired maximum number of notes)
     - `NoteLimits__MaxAttachments` = `3` (or your desired maximum attachments per note)
   - Click **Apply** and then **Confirm**
   - Restart the App Service for changes to take effect

2. **Via Azure CLI:**
   ```bash
   az webapp config appsettings set --name app-notekeeper-cscie94-ps-HW4-1 --resource-group rg_service_app_plan --settings NoteLimits__MaxNotes=10 NoteLimits__MaxAttachments=3
   ```

3. **Via Azure PowerShell:**
   ```powershell
   Set-AzWebApp -ResourceGroupName rg_service_app_plan -Name app-notekeeper-cscie94-ps-HW4-1 -AppSettings @{"NoteLimits__MaxNotes"="10"; "NoteLimits__MaxAttachments"="3"}
   ```

**Note:** The double underscore (`__`) is used to represent nested configuration sections in Azure App Service environment variables. This maps to the `NoteLimits:MaxNotes` and `NoteLimits:MaxAttachments` structure in `appsettings.json`.

---

## 9. Security Note

**No credentials for Azure Resources are included in this repository or documentation**, as specified by the requirements.

All sensitive configuration values are stored in:
- **Local Development:** User Secrets (`secrets.json`)
- **Production:** Azure App Service Environment Variables

---

## 10. Application Insights Instance

**Application Insights Instance Name:** `appi-notekeeper-cscie94-ps-HW4`

**Application Insights Connection String:**  
`InstrumentationKey=be46413f-c1d7-4824-adbc-f0fc681b5294;IngestionEndpoint=https://swedencentral-0.in.applicationinsights.azure.com/;LiveEndpoint=https://swedencentral.livediagnostics.monitor.azure.com/;ApplicationId=534fbe6b-3409-47e7-9c7c-ad4982cf9ac8`

**SDK Control Channel Authentication Key:**  
`fkobfb4qcixknvrhfiu47glh4uhzrajrg7hwwywg`

**Telemetry Implementation:**
- All validation errors are logged using `TrackTrace` with `SeverityLevel.Warning` and include validation details and the input payload
- All exceptions are logged using `TrackException` and include exception details and the input payload that caused the exception
- The input payload property name is `InputPayload` in both TrackTrace and TrackException calls
- For GET/PATCH/DELETE methods, the `InputPayload` is the noteId string value
- For POST method, the `InputPayload` is the serialized JSON of the CreateNoteRequest object
- For PATCH method, the `InputPayload` includes both the noteId and the UpdateNoteRequest object

**Note About 404 NotFound Responses:**
- 404 NotFound scenarios (when a note is not found) are considered valid business scenarios and are not logged as validation errors or exceptions
- Only actual validation failures (null/whitespace inputs, invalid GUID formats, ModelState validation failures) are logged with TrackTrace

**Note About GET Collection Endpoint:**
- The `GET /NoteKeeper` endpoint (with optional tagName filter) does not perform input validation and therefore does not log TrackTrace validation errors
- This endpoint only logs successful retrievals via TrackEvent

**Additional TrackEvent Implementation (Beyond Assignment Requirements):**
- `TrackEvent` calls have been added to several controller methods for additional tracing and monitoring purposes
- These TrackEvent calls were **not required** by the 3 assignment
- The Homework 3 assignment **did not deprecate or forbid** such additions
- TrackEvent calls are implemented in:
  - `GET /NoteKeeper` - Tracks "All Notes retrieved" with count and cost metrics
  - `POST /NoteKeeper` - Tracks "NoteCreated" with tag count, summary, and length metrics
  - `PATCH /NoteKeeper/{noteId}` - Tracks "NoteUpdated" with summary, details, and tag count metrics
  - `GET /NoteKeeper/{noteId}` - Tracks "A note is retrieved" with summary and cost metrics
- These additional telemetry events provide valuable insights into API usage patterns and performance metrics

---

## 11. Managed Identity Implementation

**This application implements Microsoft Entra ID (Azure AD) authentication using Managed Identity for passwordless database access.**

**Connection String Configuration:**
```
Server=tcp:sql-cscie94-2026-ps.database.windows.net,1433;Initial Catalog=sqldb-cscie94-2026_hw4;Encrypt=True;TrustServerCertificate=False;Connection Timeout=120;Authentication=Active Directory Default;
```

**Key Features:**
- **No credentials in connection string** - Uses `Authentication=Active Directory Default` for token-based authentication
- **Local Development** - Authenticates using Azure CLI or Visual Studio credentials via `DefaultAzureCredential`
- **Azure Deployment** - Uses the App Service's system-assigned or user-assigned managed identity
- **Enhanced Security** - Eliminates the need to store database passwords in configuration or secrets

**Implementation Details:**
- Connection string is defined in `appsettings.json` and `appsettings.managedidentities.json`
- The Azure SQL Database has been configured to accept Azure AD authentication
- The identity (`paulschwartzberg@outlook.com` for local development) has been granted appropriate database roles:
  - `db_datareader` - Read access to database tables
  - `db_datawriter` - Write access to database tables
  - `db_ddladmin` - DDL operations for `EnsureCreatedAsync()`

**Environment-Specific Configuration:**
- `ASPNETCORE_ENVIRONMENT=managedidentities` triggers the use of `appsettings.managedidentities.json`
- This enables testing managed identity authentication locally before Azure deployment

**Important:** For local development, ensure your Azure CLI or Visual Studio credentials have access to the Azure SQL Database. Your IP address must be added to the SQL Server firewall rules:
- Navigate to Azure Portal → SQL Server: `sql-cscie94-2026-ps`
- Go to **Networking** → **Firewall rules**
- Add your client IPv4 address
- Changes take up to 5 minutes to propagate

---

## 12. Azure Blob Storage Configuration

**Storage Account:** As configured in user secrets / environment variables

**Managed Identity Authentication:**
The application uses `DefaultAzureCredential` for passwordless blob storage access:
- **Local Development:** Uses developer credentials (Azure CLI, Visual Studio, VS Code)
  - `ExcludeEnvironmentCredential = true`
  - `ExcludeManagedIdentityCredential = true`
  - `ExcludeWorkloadIdentityCredential = true`
  - `ExcludeInteractiveBrowserCredential = true`
  - Tenant ID scoped for SharedTokenCache, VisualStudioCode, and VisualStudio credentials
  
- **Azure Deployment:** Uses managed identity credentials only
  - `ExcludeVisualStudioCredential = true`
  - `ExcludeVisualStudioCodeCredential = true`
  - `ExcludeAzureCliCredential = true`
  - `ExcludeAzurePowerShellCredential = true`
  - `ExcludeAzureDeveloperCliCredential = true`
  - `ExcludeWorkloadIdentityCredential = true`
  - `ExcludeInteractiveBrowserCredential = true`

**Container Naming:**
- Each note has its own blob container
- Container name = note's GUID ID (lowercase)
- Access level: Private (no anonymous access)

**Blob Metadata:**
- Each blob has a `noteid` metadata property set to the note's ID
- Case-insensitive metadata key per Azure specification

**Retry Logic:**
- Container creation includes retry logic for Azure's "ContainerBeingDeleted" transient error
- Up to 10 attempts with 4-second delays between retries
- Required because Azure takes up to 30 seconds to fully delete a container before allowing recreation

**Configuration Settings:**
- `StorageAccountSettings:Url` - Blob service endpoint (e.g., `https://<account>.blob.core.windows.net/`)
- `StorageAccountSettings:TenantId` - Azure AD tenant ID (local development only)
- `StorageAccountSettings:AccountName` - Storage account name

---

## 13. Bicep Extra Credit — Infrastructure as Code Deployment

### Overview

The `BicepFiles4XC` folder (added to the solution under **BicepFiles4XC/** solution folder) contains a complete Bicep IaC deployment that:

| Step | Action | Resource | Target Resource Group |
|------|--------|----------|----------------------|
| (a) | Creates resource group | `rg_03-assignment` | *(subscription level)* |
| (b) | Deploys Azure Blob Storage | `stHW4bicepextracredit` | `rg_03-assignment` |
| (c) | Updates App Service Plan to B2 | `asp-cscie94` | `rg_service_app_plan` |
| (d) | Deploys new App Service | `app-HW4-bicep-extra-credit` | `rg_03-assignment` |

### File Structure

```
Bicep/BicepFiles4XC/
├── main.bicep                   ← Single entry point (subscription scope)
├── createStorageAccount.bicep   ← Module: deploys Storage Account
├── updateAppServicePlan.bicep   ← Module: updates asp-cscie94 B1 → B2
└── createAppService.bicep       ← Module: deploys new App Service
```

### How to Deploy

**Prerequisites:**
- Azure CLI installed and logged in (`az login`)
- Bicep CLI (included with Azure CLI 2.20+)
- Contributor or Owner role on the subscription

#### Azure CLI

```bash
cd C:\Users\schwa\Documents\H_DCE\cloud_computing_openai_e_94\assignments\03-Assignment\HW4NoteKeeperEx1\Bicep\BicepFiles4XC

az deployment sub create `
  --name "BicepXC_$(Get-Date -Format 'yyyyMMddHHmmss')" `
  --location eastus `
  --template-file main.bicep
```
 
#### PowerShell

```powershell
cd C:\Users\schwa\Documents\H_DCE\cloud_computing_openai_e_94\assignments\03-Assignment\HW4NoteKeeperEx1\Bicep\BicepFiles4XC

New-AzSubscriptionDeployment `
  -Name ("BicepXC_" + (Get-Date -Format "yyyyMMddHHmmss")) `
  -Location 'eastus' `
  -TemplateFile 'main.bicep'
```

or 

```powershell

 az deployment sub create `
     --name ("BicepXC_" + (Get-Date -Format "yyyyMMddHHmmss")) `
     --location eastus `
     --template-file "C:\Users\schwa\Documents\H_DCE\cloud_computing_openai_e_94\assignments\03-Assignment\HW4NoteKeeperEx1\Bicep\BicepFiles4XC\main.bicep"
 ```
### How to delete the deployed resources

```powershell

az group delete -n rg_03-assignment --yes --no-wait

 ```

#### Notes
- The App Service Plan update is **idempotent**: if `asp-cscie94` is already B2, no change is made.
- The new App Service `app-HW4-bicep-extra-credit` is linked to `asp-cscie94` (cross-resource-group reference via full resource ID).
- To deploy async, append `--no-wait` to the Azure CLI command.

---

## Additional Technical Notes

### API Endpoints

**Note Management:**
- `GET /NoteKeeper` - Retrieve all notes (optional `tagName` query parameter for filtering)
- `GET /NoteKeeper/{noteId}` - Retrieve a specific note by ID
- `POST /NoteKeeper` - Create a new note with AI-generated tags
  - Returns 201 (Created) on success
  - Returns 400 (Bad Request) if validation fails
  - Returns 403 (Forbidden) if the MaxNotes limit has been reached
  - Returns 500 (Internal Server Error) on unexpected errors
- `PATCH /NoteKeeper/{noteId}` - Update an existing note (regenerates tags if details change)
- `DELETE /NoteKeeper/{noteId}` - Delete a note

**Attachment Management (HW4):**
- `PUT /notes/{noteId}/attachments/{attachmentId}` - Upload or update an attachment
  - Returns 201 (Created) with Location header for new attachments
  - Returns 204 (No Content) for updates to existing attachments
  - Returns 400 (Bad Request) for invalid GUID format
  - Returns 403 (Forbidden) when MaxAttachments limit is reached (new uploads only)
  - Works purely with blob storage (no database check)
- `GET /notes/{noteId}/attachments/{attachmentId}` - Retrieve a single attachment
  - Returns 200 (OK) with file stream and Content-Disposition header
  - Returns 400 (Bad Request) for invalid GUID format
  - Returns 404 (Not Found) if attachment or container doesn't exist
- `GET /notes/{noteId}/attachments` - Retrieve all attachment metadata for a note
  - Returns 200 (OK) with array of attachment info (empty array if no attachments)
  - Returns 400 (Bad Request) for invalid GUID format
  - Returns 404 (Not Found) if container doesn't exist (goes beyond spec for consistency)
- `DELETE /notes/{noteId}/attachments/{attachmentId}` - Delete an attachment
  - Returns 204 (No Content) on successful deletion or if attachment doesn't exist (idempotent)
  - Returns 400 (Bad Request) for invalid GUID format
  - Returns 404 (Not Found) if the container (note) doesn't exist
  - Returns 500 (Internal Server Error) if deletion fails
  - Works purely with blob storage (no database check)

### Note Limits
The application supports configurable limits for notes and attachments:

**MaxNotes:**
- **Default Value:** 10 notes
- **Configuration:** Set via `NoteLimits:MaxNotes` in `appsettings.json` or `NoteLimits__MaxNotes` in Azure App Service environment variables
- **Behavior:** When the limit is reached, POST requests to create new notes will return 403 (Forbidden) with details about the current limit

**MaxAttachments (HW4):**
- **Default Value:** 3 attachments per note
- **Configuration:** Set via `NoteLimits:MaxAttachments` in `appsettings.json` or `NoteLimits__MaxAttachments` in Azure App Service environment variables
- **Behavior:** When the limit is reached, PUT requests to upload new attachments will return 403 (Forbidden)
- **Note:** Updating an existing attachment bypasses the limit check (only new uploads are counted against the limit)

### Tag Generation
The application uses Azure OpenAI's GPT-5-mini model to automatically generate any number of tags (no max set)
for each note based on its details. Tags are:
- Concise (single-word or two-word)
- Lowercase
- Relevant to the note content
- Automatically generated when a note is created or when details are updated

### Technology Stack
- ASP.NET Core 10.0 Web API
- Azure OpenAI (GPT-5-mini deployment)
- Entity Framework Core 9.0 with Azure SQL Database
- Azure Blob Storage (for note attachments)
- Application Insights (telemetry and monitoring)
- Managed Identity / DefaultAzureCredential (passwordless authentication)
- Swashbuckle/Swagger for API documentation

---

## 4.2. Homework 4 New Features

### 4.2.1 NoteKeeperZipAttachmentController

A new controller (`HW4NoteKeeperEx1.Controllers.NoteKeeperZipAttachmentController`) was added at route `notes/{noteId}` with five operations:

#### POST `/notes/{noteId}/attachmentzipfiles` — Request Zip Creation (§1.1)
- Validates `noteId` is a valid GUID → 400 Bad Request if not
- Verifies note exists in Azure SQL → 404 Not Found if not
- Checks the attachment blob container has ≥ 1 blob → 204 No Content if 0 blobs
- Generates a unique `zipFileId` (`{Guid}.zip`) and enqueues a JSON message to the `attachment-zip-requests` queue
- Returns 202 Accepted with a `Location` header pointing to the future zip download URL

#### DELETE `/notes/{noteId}/attachmentzipfiles/{zipFileId}` — Delete Zip File (§1.2)
- Validates `noteId` (GUID) → 400; verifies note exists → 404
- Deletes the zip blob from the `{noteId}-zip` container (idempotent)
- Returns 204 No Content whether or not the blob existed

#### GET `/notes/{noteId}/attachmentzipfiles/{zipFileId}` — Retrieve Zip by ID (§1.3)
- Validates `noteId` (GUID) → 400; verifies note exists → 404
- Returns the zip blob as `application/zip` stream → 200 OK
- Returns 404 Not Found if the zip blob does not exist

#### GET `/notes/{noteId}/attachmentzipfiles` — Retrieve All Zip Files (§1.4)
- Validates `noteId` (GUID) → 400; verifies note exists → 404
- Returns an array of `ZipBlobInfo` DTOs → 200 OK (empty array if container doesn't exist yet)

#### DELETE `/notes/{noteId}` — Enhanced Note Delete (§1.5)
- Validates `noteId` (GUID) → 400; verifies note exists → 404
- Deletes the attachment blob container (`{noteId}`) — idempotent
- Deletes the zip blob container (`{noteId}-zip`) — idempotent
- Removes the Note (and cascade-deletes Tags) from Azure SQL
- Returns 204 No Content

### 4.2.2 AzureStorageService Extensions

`HW4NoteKeeperEx1.Services.AzureStorageService` was extended with:
- `QueueServiceClient` dependency (injected as singleton; URI-based managed identity)
- `EnqueueZipRequestAsync(noteId, zipFileId)` — sends JSON message to `attachment-zip-requests` queue
- `ListZipBlobsAsync(noteId)` — lists blobs in `{noteId}-zip` container; returns `null` if container doesn't exist
- `DownloadZipBlobAsync(noteId, zipFileId)` — downloads a zip blob; returns `null` if not found
- `DeleteZipBlobAsync(noteId, zipFileId)` — deletes a single zip blob (idempotent)
- `DeleteContainerIfExistsAsync(containerName)` — deletes any named container and all its blobs
- `ZipContainerExistsAsync(noteId)` — checks existence of the `{noteId}-zip` container

### 4.2.3 New Models and DTOs

| File | Purpose |
|------|---------|
| `HW4NoteKeeperEx1/Models/ZipRequest.cs` | Queue message payload (`NoteId`, `ZipFileId`) |
| `HW4NoteKeeperEx1/RequestAndResultObjects/ZipBlobInfo.cs` | DTO returned by the GET all zip files endpoint |

### 4.2.4 HW4AzureFunctionsEx1 Project (§3)

A new Azure Functions project (`HW4AzureFunctionsEx1`, `net8.0`, isolated worker model v4) was added to the solution.

- **Function name:** `AttachmentZipFunction`
- **Trigger:** `QueueTrigger` on `attachment-zip-requests` queue in `st4hw3` storage account
- **Connection name:** `AttachmentZipRequests` (configured as `AttachmentZipRequests__queueServiceUri` via managed identity)
- **Logic:**
  1. Deserialises the `ZipRequest` JSON payload
  2. Lists all blobs in the `{noteId}` attachment container
  3. If container is missing or empty — logs a warning and exits (no zip created)
  4. Builds an in-memory `ZipArchive` containing all attachment blobs (using `System.IO.Compression`)
  5. Uploads the zip to the `{noteId}-zip` container as blob named `{zipFileId}`, content-type `application/zip`
- **Error handling:** Any exception causes retry; after `maxDequeueCount` (5) retries the message moves to `attachment-zip-requests-poison`
- **Managed identity (EC3):** Uses `DefaultAzureCredential` for all blob and queue operations; `AzureWebJobsStorage__serviceUri` and `AttachmentZipRequests__queueServiceUri` point to `st4hw3`
- **Azure deployment:** Function App `func-HW4`; managed identity `id-dbadmin` (Owner role)

#### local.settings.json (HW4AzureFunctionsEx1)
```json
{
  "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
  "AzureWebJobsStorage__serviceUri":    "https://st4hw3.blob.core.windows.net",
  "AttachmentZipRequests__queueServiceUri": "https://st4hw3.queue.core.windows.net",
  "StorageBlobServiceUri":  "https://st4hw3.blob.core.windows.net",
  "StorageQueueServiceUri": "https://st4hw3.queue.core.windows.net"
}
```

### 4.2.5 Testing

Two new E2E test classes were added to `HW4NoteKeeperEx1.Tests`:

| Test Class | What it tests |
|-----------|--------------|
| `NoteKeeperZipAttachmentE2ETests` | All 5 controller endpoints + full cycle (POST→GET list→GET download→DELETE zip) |
| `AttachmentZipFunctionE2ETests` | Azure Function end-to-end: enqueues real messages, verifies zip blobs in Azure Blob Storage |

Both test classes are marked `[Collection("Sequential")]` and `[Trait("Category", "E2E")]` and require the app and function app to be deployed.  They must **not** be run automatically.

The `HW4NoteKeeperEx1.Tests.csproj` was updated to include `Azure.Storage.Queues` for queue interaction in function E2E tests.

### 4.2.6 Azure Infrastructure Summary

| Resource | Name | Purpose |
|----------|------|---------|
| Azure SQL Database | `sqldb-cscie94-2026_hw4` | Notes + Tags storage |
| Azure Storage Account | `st4hw3` | Blob containers for attachments, zip containers, queues |
| Azure Storage Queue | `attachment-zip-requests` | Triggers the Azure Function for zip creation |
| Azure Storage Queue | `attachment-zip-requests-poison` | Dead-letter queue for failed function executions |
| Azure App Service | `app-notekeeper-cscie94-ps-hw4` | Hosts the `HW4NoteKeeperEx1` Web API |
| Azure Function App | `func-HW4` | Hosts the `AttachmentZipFunction` |
| Managed Identity | `id-dbadmin` | Used by both App Service and Function App for passwordless auth |

### 4.2.7 Technology Stack (HW4 additions)
- `System.IO.Compression.ZipArchive` — in-memory zip archive creation
- `Azure.Storage.Queues` — queue client for enqueuing/dequeuing zip requests
- Azure Functions Isolated Worker v4 (`net8.0`) — serverless zip processing
- `DefaultAzureCredential` — managed identity for all Azure resource access (EC3)

---

## 4.2.8 Azure Storage Container for Function App Deployment

During deployment of the `HW4AzureFunctionsEx1` Azure Function to `func-HW4`, a new Azure Blob Storage container was required for storing the function app deployment package.

- **Container name:** `app-package-func-hw4`
- **Storage account:** `st4hw3`
- **Purpose:** Holds the Azure Function App deployment zip package (used by Azure when publishing via Visual Studio / Azure CLI)
- **Created by:** Paul Schwartzberg during HW4 Azure Function deployment

- **IMPORTANT:** This container is protected and must **never** be deleted during storage seeding. Listed in `StorageOperationalSettings.ProtectedContainers` in `appsettings.json`.

---

## 4.2.9 Database: sqldb-cscie94-2026_hw4

Per the HW4 requirements (page 1), a **new, dedicated Azure SQL database** was created for HW4. The old `sqldb-cscie94-2026` database from HW3 is **no longer used**.

| Setting | Value |
|---------|-------|
| Database name | `sqldb-cscie94-2026_hw4` |
| Connection string location | `appsettings.json` → `ConnectionStrings:DefaultConnection` |
| Azure App Service override | App Setting `ConnectionStrings__DefaultConnection` |
| Azure Function App override | App Setting `ConnectionStrings__DefaultConnection` |

The managed identity `id-dbadmin` must have `db_owner` on this database:
```sql
CREATE USER [id-dbadmin] FROM EXTERNAL PROVIDER;
ALTER ROLE db_owner ADD MEMBER [id-dbadmin];
```

---

## 4.2.10 Seeding Enhancements: Queue Clearing

When `HW4NoteKeeperEx1` starts (or is deployed), `DbInitializer.InitializeAsync()` now also clears both Azure Storage queues so stale messages do not trigger the Azure Function after a fresh deploy.

**Seeding order:**
1. Run EF Core migrations
2. Delete all rows from `Note` and `Tag` tables
3. Delete all blob containers (except protected ones in `ProtectedContainers`)
4. **Clear all messages from `attachment-zip-requests` queue**
5. **Clear all messages from `attachment-zip-requests-poison` queue**
6. Seed the 4 default notes with attachments

**Implementation:**
- `ZipPoisonQueueName` added to `StorageOperationalSettings` and `appsettings.json`
- `ClearQueuesAsync()` added to `IAzureStorageInitializer` and implemented in `AzureStorageInitializer`
- `AzureStorageInitializer` now receives `QueueServiceClient` via constructor injection
- Handles missing queues gracefully (log warning, non-fatal)

---

## 4.2.11 Enhanced Note Delete (§1.5) — Container Cleanup Detail

The enhanced note delete endpoint `DELETE notes/{noteId}` (`NoteKeeperZipAttachmentController.DeleteNoteWithAllAssets`) deletes **all** data associated with a note:

| Step | What is deleted |
|------|----------------|
| 1 | Attachment blob container `{noteId}` and all its blobs |
| 2 | Zip blob container `{noteId}-zip` and all its zip blobs |
| 3 | Note record from `Note` table (SQL database) |
| 4 | All `Tag` records for the note (cascade-deleted by EF Core) |

Both container deletes use `DeleteContainerIfExistsAsync()` — idempotent, no error if container absent.

**Route:** `DELETE /notes/{noteId}` — distinct from `DELETE /NoteKeeper/{noteId}` in `NoteKeeperController` which only removes the DB row (no storage cleanup).

---

## 4.2.12 E2E Test Cleanup (AttachmentZipFunctionE2ETests)

`DisposeAsync()` performs belt-and-suspenders cleanup after every test (pass or fail):

1. Calls `DELETE notes/{noteId}` (enhanced delete) for every note created — removes DB row, tags, attachment container, and zip container via the API.
2. Additionally calls `BlobServiceClient.DeleteIfExistsAsync()` directly on every container in `_containerNamesToDelete` — covers the case where the API call fails silently.

Containers tracked per test:
- Attachment container `{noteId}` — added inside `CreateTestNoteAsync()`
- Zip container `{noteId}-zip` — added inside each test that expects a zip

---

## 4.2.13 Session Fixes — Azure Function and Seeding Corrections

### Protected Containers During Seeding

`DeleteAllContainersAsync()` in `AzureStorageInitializer.cs` now skips:
- **3 named containers** configured in `StorageOperationalSettings.ProtectedContainers`:
  - `app-package-func-hw4` — Azure Function deployment package
  - `azure-webjobs-hosts` — Azure Functions runtime host metadata
  - `azure-webjobs-secrets` — Azure Functions secrets/keys
- **`$`-prefixed containers** (e.g., `$logs`, `$blobchangefeed`) — Azure system containers

These containers must NEVER be deleted, especially during seeding, as deleting them breaks the deployed Azure Function.

### AttachmentZipHttpTestFunction — Debug-Only Compilation

`AttachmentZipHttpTestFunction.cs` is wrapped in `#if DEBUG` / `#endif`. This HTTP-triggered test function is only compiled in Debug builds and is **excluded from production deployments** (VS Publish uses Release configuration).

### SQL Table Name in AttachmentZipProcessor

The raw SQL query in `NoteExistsInDatabaseAsync()` uses `Note` (the actual SQL table name configured via `modelBuilder.Entity<Note>().ToTable("Note")`), NOT `Notes` (which is only the EF Core `DbSet` property name).

```sql
SELECT COUNT(1) FROM Note WHERE Id = @NoteId
```

### Azure Function Environment Variables (func-HW4)

Required App Settings (in Azure Portal → Environment variables → App Settings tab):

| Setting | Purpose |
|---------|---------|
| `ConnectionStrings__DefaultConnection` | SQL connection string with `Authentication=Active Directory Default` |
| `AzureWebJobsStorage__blobServiceUri` | Managed identity storage access |
| `AzureWebJobsStorage__clientId` | Managed identity client ID |
| `AzureWebJobsStorage__credential` | `managedidentity` |
| `AzureWebJobsStorage__queueServiceUri` | Queue endpoint for managed identity |
| `AzureWebJobsStorage__tableServiceUri` | Table endpoint for managed identity |
| `AttachmentZipRequests__clientId` | Queue trigger managed identity |
| `AttachmentZipRequests__credential` | `managedidentity` |
| `AttachmentZipRequests__queueServiceUri` | Queue endpoint for zip requests |
| `StorageBlobServiceUri` | Blob service URI for processor |

**Important:** Visual Studio Zip Deploy does NOT sync `local.settings.json` to Azure. Settings configured in Azure Portal remain intact after publish.

---

## 4.2.14 Extra Credit — HW4NoteKeeperEx1

The solution ending with **Ex1** (`HW4NoteKeeperEx1`) is being updated with:

> **Extra Credit 1: Add support for a job status tracking table.**

This feature adds a database table to track the status of background zip-creation jobs, enabling the API to report job progress and completion status to clients.

---

## 4.2.15 Extra Credit 1 — Azure Resources Created

### Azure Table: Jobs

An Azure Table named **`Jobs`** was created in the `st4hw3` storage account for tracking job status.

| Setting | Value |
|---------|-------|
| Table name | `Jobs` |
| Storage account | `st4hw3` |
| URL | `https://st4hw3.table.core.windows.net/Jobs` |
| Purpose | Track status of background zip-creation jobs (Extra Credit 1) |

### Azure Queue: attachment-zip-requests-ex1

A new Azure Storage Queue named **`attachment-zip-requests-ex1`** was created in the `st4hw3` storage account, dedicated to the Ex1 solution.

| Setting | Value |
|---------|-------|
| Queue name | `attachment-zip-requests-ex1` |
| Storage account | `st4hw3` |
| URL | `https://st4hw3.queue.core.windows.net/attachment-zip-requests-ex1` |
| Poison queue | `attachment-zip-requests-ex1-poison` (auto-created by Azure Functions runtime) |
| Purpose | Separate queue for Ex1 zip requests, so Ex1 and the original solution do not interfere with each other |

**Note:** The original solution continues to use `attachment-zip-requests`. The Ex1 solution uses `attachment-zip-requests-ex1`. Both queues coexist in the same storage account.

The `StorageOperationalSettings` in `appsettings.json` (Ex1 only) was updated:
- `ZipRequestsQueueName`: `attachment-zip-requests-ex1`
- `ZipPoisonQueueName`: `attachment-zip-requests-ex1-poison`
- `JobsTableName`: `Jobs`

---

## 4.2.16 Extra Credit 1 — Implementation Details

### Overview
Extra Credit 1 adds job status tracking for zip-creation operations. Every time a zip is requested, a row is inserted into the Azure Storage `Jobs` table tracking the lifecycle: **Queued → InProgress → Completed/Failed**.

### Jobs Table Schema (Azure Table Storage)

| Column | Value | Description |
|--------|-------|-------------|
| PartitionKey | noteId | Groups all jobs for a note together |
| RowKey | zipFileId | Unique identifier for the zip file (e.g., `{guid}.zip`) |
| Status | `Queued`, `InProgress`, `Completed`, `Failed` | Current job state |
| StatusDetails | Formatted string | Human-readable status with IDs |
| Timestamp | DateTimeOffset | Azure Table auto-managed timestamp |

### StatusDetails Format (per requirement §2.4.1–2.4.4)
- Queued: `"Queued: Zip File Id: {zipFileId} NoteId: {noteId}"`
- InProgress: `"In Progress: Zip File Id: {zipFileId} NoteId: {noteId}"`
- Completed: `"Completed: zipFileId: {zipFileId} containerId: {containerId}"`
- Failed: `"Failed: Zip File Id: {zipFileId} NoteId: {noteId}"`

### New Files Created

| File | Project | Purpose |
|------|---------|---------|
| `Models\JobEntity.cs` | Both Web API and Functions | Azure Table entity class |
| `RequestAndResultObjects\JobStatusResponse.cs` | Web API | API response DTO |
| `Services\JobsTableService.cs` | Web API | CRUD operations on Jobs table |
| `TableStorageHelper.cs` | Functions | Dual-path TableClient creation |
| `Controllers\NoteKeeperZipAttachmentControllerEx1.cs` | Web API | Two new GET endpoints |

### New API Endpoints (NoteKeeperZipAttachmentControllerEx1)

| Method | Route | Description | Returns |
|--------|-------|-------------|---------|
| GET | `notes/{noteId}/attachmentzipfiles/jobs/{zipFileId}` | Get specific job status (§2.5) | 200 OK / 404 |
| GET | `notes/{noteId}/attachmentzipfiles/jobs` | Get all job statuses for a note (§3) | 200 OK / 404 |

### Enhanced POST — RequestZipCreation (§2.3.1)
After enqueuing the zip request, the POST method now inserts a **Queued** row into the Jobs table.

### Enhanced Azure Function — AttachmentZipProcessor (§2.3.2–2.3.4, §4.1.5)
The processor now:
1. **Check 1 (§4.1.5):** Verifies Queued row exists before starting → aborts if deleted by a concurrent DELETE
2. Updates status to **InProgress** (§2.3.2)
3. Performs zip creation (download blobs → build archive)
4. **Check 2 (§4.1.5):** Re-verifies row exists before creating zip container → aborts if deleted
5. Uploads zip blob, updates to **Completed** (§2.3.3) with containerId in StatusDetails
6. On any failure, updates to **Failed** (§2.3.4) using safe update (no exception masking)

### Enhanced DELETE — DeleteNoteWithAllAssets (§4.1–4.1.5)
The DELETE method now:
1. **§4.1.4:** Checks if any job has Status=InProgress → returns **409 Conflict** if so
2. **§4.1:** Deletes all Jobs table rows for the noteId
3. **§4.1.2:** Logs info (not error) if no rows found
4. **§4.1.3:** Logs error on table deletion failure but continues with other operations
5. Proceeds with existing container and database deletion

### Seeding Enhancement
`DbInitializer.InitializeAsync()` now includes Step 3c: `ClearJobsTableAsync()` — clears all rows from the Jobs table to ensure clean state after redeployment.

### DI Registration
- **Web API Program.cs:** Registers `TableClient` singleton (via `RegisterTableClient()` using `DefaultAzureCredential`) and `JobsTableService` as scoped
- **Functions Program.cs:** Registers `TableStorageHelper` as singleton

### New App Service for Ex1
| Setting | Value |
|---------|-------|
| App name | `app-notekeeper-cscie94-ps-hw4-ex1` |
| Runtime | .NET 10 |
| OS | Linux |
| SKU | Basic B2 |
| Region | Sweden Central |


---

## 4.3 Queue Routing: Two POST Methods, Two Queues

### Background
The Ex1 solution coexists with the original HW4NoteKeeper solution. Both share the same Azure storage account (st4hw3) but use **separate queues** and separate Function App triggers so they do not interfere with each other.

### Old POST — RequestZipCreation (NoteKeeperZipAttachmentController)
| Property | Value |
|----------|-------|
| Controller | NoteKeeperZipAttachmentController |
| Route | POST notes/{noteId}/attachmentzipfiles |
| Target queue | ttachment-zip-requests |
| Jobs table row | **Not inserted** — legacy flow has no job tracking |
| Triggered function | AttachmentZipFunction (listens on ttachment-zip-requests) |

The old POST was **deliberately kept without Jobs table interaction**. It enqueues to the original ttachment-zip-requests queue and returns a 202 Accepted with a Location header pointing to the zip blob URL. This keeps backward compatibility with the original HW4 assignment.

The method AzureStorageService.EnqueueLegacyZipRequestAsync was added (alongside EnqueueZipRequestAsync) so the two queue names are independently configurable via StorageOperationalSettings:

| Setting property | Default value | Queue |
|-----------------|---------------|-------|
| ZipRequestsQueueName | ttachment-zip-requests-ex1 | Ex1 function |
| ZipRequestsLegacyQueueName | ttachment-zip-requests | Legacy function |

### New POST — RequestZipCreationEx1 (NoteKeeperZipAttachmentControllerEx1)
| Property | Value |
|----------|-------|
| Controller | NoteKeeperZipAttachmentControllerEx1 |
| Route | POST notes/{noteId}/attachmentzipfilesex1 |
| Target queue | ttachment-zip-requests-ex1 |
| Jobs table row | **Inserted** as Status=Queued before enqueuing |
| Triggered function | AttachmentZipFunctionEx1 (listens on ttachment-zip-requests-ex1) |

The new POST first inserts a Queued row into the Jobs table (so status can be tracked immediately), then enqueues to ttachment-zip-requests-ex1. It returns 202 Accepted with a Location header pointing to the job-status endpoint: 
otes/{noteId}/attachmentzipfiles/jobs/{zipFileId}.

### NoteKeeperZipAttachmentControllerEx1 — All Endpoints
| Method | Route | Description | Returns |
|--------|-------|-------------|---------|
| POST | 
otes/{noteId}/attachmentzipfilesex1 | Create zip job (Ex1) — inserts Jobs row, enqueues to ex1 queue | 202 / 204 (no attachments) / 404 |
| GET | 
otes/{noteId}/attachmentzipfiles/jobs/{zipFileId} | Get specific job status | 200 / 404 |
| GET | 
otes/{noteId}/attachmentzipfiles/jobs | Get all job statuses for a note | 200 / 404 |

### Why the Route Differs
The base route of NoteKeeperZipAttachmentControllerEx1 is 
otes/{noteId}/attachmentzipfiles/jobs.
The new POST uses an absolute route override ([HttpPost("/notes/{noteId}/attachmentzipfilesex1")]) with x1 appended to the path to avoid conflicts with the old POST at 
otes/{noteId}/attachmentzipfiles and with the GET collection at 
otes/{noteId}/attachmentzipfiles/jobs.

### Moq / Unit Testing Note
AzureStorageService methods GetBlobCountAsync, EnqueueZipRequestAsync, EnqueueLegacyZipRequestAsync and JobsTableService.InsertQueuedJobAsync are marked irtual so Moq can mock them in unit tests without requiring interface extraction. 11 unit tests in NoteKeeperZipAttachmentControllerTests cover both controllers and all queue-routing scenarios.

---

## 4.4 Jobs Table Design Decision: Single Row Update (Not Insert-Per-Status)

### Design Decision
The Azure Function (`AttachmentZipProcessor`) **updates the same Jobs table row in-place** as the job progresses through its lifecycle, rather than inserting a new row for each status change.

### Row Lifecycle
1. **Web API POST** (`InsertQueuedJobAsync`) — Inserts a single row with `Status = "Queued"`
   - PartitionKey = noteId (lowercase), RowKey = zipFileId
2. **Azure Function** — Updates that same row:
   - `Status = "InProgress"` (before zip creation begins)
   - `Status = "Completed"` (after successful zip upload) or `Status = "Failed"` (on error)

### Why Update Instead of Insert New Rows
- **Simpler querying**: GET job status returns a single row per zipFileId — no need to query for the "latest" status
- **Cleaner storage**: One row per job, not 3+ rows per job
- **ETag-based concurrency**: The Function uses conditional updates (`UpdateEntityAsync` with ETag) to detect if a DELETE operation cancelled the job between status transitions (§4.1.5)
- **Consistent with the GET endpoints**: `GetJobStatus` and `GetAllJobStatuses` each return one entry per zipFileId, which maps directly to one row in the table

### What the Final Row Looks Like
After successful processing, the Jobs table row contains:
| Field | Example Value |
|-------|---------------|
| PartitionKey | `6d836d96-5eca-49ef-ada9-f1f6060961d3` |
| RowKey | `b380ac58-2105-48aa-94d2-db0b83b12dc6.zip` |
| Status | `Completed` |
| StatusDetails | `Completed: zipFileId: b380ac58-... containerId: 6d836d96-...-zip` |
| Timestamp | (auto-updated by Azure Table Storage on each write) |
