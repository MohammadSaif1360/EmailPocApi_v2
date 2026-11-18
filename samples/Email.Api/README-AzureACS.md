# Email.Api — run it with Azure Communication Services (ACS)

A Web API that sends real email through ACS using the reusable `Company.Email` library.
Test it from the Swagger page in your browser.

---

## Part 1 — Run it first WITHOUT Azure (2 minutes)

This checks that everything builds and runs. Emails are printed in the console window instead of being sent.

1. Open `EmailPoc.sln` in Visual Studio.
2. In Solution Explorer, right-click **Email.Api** → **Set as Startup Project**.
3. Press **F5**. The browser opens `https://localhost:7180/swagger`.
   - If the browser warns about the certificate, run `dotnet dev-certs https --trust` once and restart.
4. In Swagger, open **POST /api/email/send** → **Try it out** → paste:

   ```json
   {
     "to": ["you@example.com"],
     "subject": "First test",
     "htmlBody": "<h2>Hello</h2><p>From the Email API.</p>"
   }
   ```

5. Click **Execute**. You should get **200** and `"provider": "Console"`, and the email appears in the console window.

---

## Part 2 — Set up Azure Communication Services (one time, ~15 minutes)

You need an Azure subscription where you can create resources.

### Step 1: Create an Email Communication Service
1. Azure portal → **Create a resource** → search **Email Communication Services** → **Create**.
2. Choose subscription, resource group (e.g. `rg-email-poc`), a name (e.g. `email-poc-ecs`) and a **Data location**. Remember the data location; Step 3 must use the same one.
3. **Review + create** → **Create**.

### Step 2: Add a free Azure-managed domain
1. Open the new Email Communication Service → **Provision domains** (or **Domains**) → **Add domain** → **Azure managed domain**.
2. Wait until it is provisioned. You get a domain like `xxxxxxxx.azurecomm.net`.
3. Open that domain → **MailFrom addresses**. Copy the address, usually **`DoNotReply@xxxxxxxx.azurecomm.net`**. This is your sender.

> An Azure-managed domain is meant for testing: it has a low sending limit and emails may land in Spam. For production, add your company's own domain (needs DNS records) instead.

### Step 3: Create a Communication Services resource
1. **Create a resource** → search **Communication Services** → **Create**.
2. Same resource group, a name (e.g. `email-poc-acs`), and the **same Data location** as Step 1.

### Step 4: Connect the domain
1. Open the Communication Services resource → **Email** → **Domains** → **Connect domain**.
2. Pick the Email Communication Service and the `azurecomm.net` domain from Step 2 → **Connect**.

### Step 5: Copy the connection string
1. Communication Services resource → **Settings** → **Keys**.
2. Copy the **Primary connection string**. It looks like `endpoint=https://email-poc-acs.<region>.communication.azure.com/;accesskey=...`

---

## Part 3 — Connect the API to ACS

### Step 6: Store the connection string as a user secret (never in appsettings.json)
In Visual Studio: right-click **Email.Api** → **Manage User Secrets**. Replace the contents with:

```json
{
  "Email:AzureAcs:ConnectionString": "endpoint=https://...;accesskey=..."
}
```

Or, from the solution folder in PowerShell:

```powershell
dotnet user-secrets set "Email:AzureAcs:ConnectionString" "endpoint=https://...;accesskey=..." --project samples/Email.Api
```

User secrets live in your Windows user profile, outside the project folder, so they never get committed to Git.

### Step 7: Edit `samples/Email.Api/appsettings.json`

```json
"EmailApi": {
  "DefaultFrom": "DoNotReply@xxxxxxxx.azurecomm.net",   ← your MailFrom from Step 2
  "DefaultFromName": "Email POC"
},
"Email": {
  "DefaultProvider": "AzureAcs",                          ← was "Console"
  ...
}
```

### Step 8: Send a real email
1. Press **F5** again.
2. **GET /api/email/providers** → **Try it out** → **Execute**. You should see `"defaultProvider": "AzureAcs"`.
3. **POST /api/email/send** with your own email address in `to` → **Execute**.
4. Expected: **200**, `"success": true`, `"provider": "AzureAcs"`, plus a `messageId` (the ACS operation ID).
5. Check your inbox **and the Spam folder**.

Try **POST /api/email/send-with-attachments** too: fill `To`, `Subject`, `TextBody`, and click **Add item** under `Attachments` to pick files.

`"WaitForCompletion": true` in appsettings makes the API wait (a few seconds) until ACS reports the final status, so a bad sender or recipient shows up as an error right away. Set it to `false` for production: the API then returns as soon as ACS accepts the email.

---

## Common errors

| What you see | Cause | Fix |
|---|---|---|
| 400 `AzureAcs: ConnectionString is not configured` | User secret missing or key name typed wrong | Repeat Step 6. The key must be exactly `Email:AzureAcs:ConnectionString` |
| 400 `No From address given...` | `EmailApi:DefaultFrom` empty and no `from` in the request | Step 7 |
| 502 with `401` / `Denied` / `Unauthorized` | Wrong or rotated access key | Copy the connection string again (Step 5) |
| 502 with `DomainNotLinked` | Domain not connected to the Communication Services resource | Step 4 |
| 502 mentioning sender / `InvalidSenderUserName` | `from` is not the MailFrom address of a connected domain | Use exactly the address from Step 2 |
| 502 with `429` / `TooManyRequests` | Azure-managed domain sending limit reached | Wait and retry, or use your own domain |
| 200 but no email arrives | Spam folder, or the recipient server rejected it | Check Spam; set `WaitForCompletion: true` to see the final status |
| `You must install or update .NET` on F5 | .NET 8 runtime not installed | Install the .NET 8 runtime, or change `net8.0` to `net10.0` in `Directory.Build.props` |

---

## Switching provider later (the plug-and-play proof)

No code change is needed:
- **To SMTP:** fill `Email:Smtp` (host, port, user, password as a user secret) and set `"DefaultProvider": "Smtp"`.
- **ACS with SMTP backup:** keep `"DefaultProvider": "AzureAcs"` and set `"FallbackProviders": ["Smtp"]`.
- **Back to testing:** `"DefaultProvider": "Console"`.

Or switch per request by putting `"provider": "Console"` (or `"Smtp"`) in the request body.
