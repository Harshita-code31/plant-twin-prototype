# AbhyasAR admin dashboard

A static site with no build step. Admins use it to manage worker logins, follow training progress and certification, and verify certificates.

| Page | What it's for |
|---|---|
| **Overview** | Key numbers and charts for a chosen period and training: activity over time, certification progress, pass rate per training part, the fire-scenario steps trainees miss most, and quiz score spread. Every chart has a **Table** view. |
| **Workers** | Everyone's login status and certification per training (Fire Safety, Machine Training), with search, filters, sorting and CSV export. **+ Add workers** creates one or many logins at once. |
| **Worker detail** | Progress per training (each fire scenario, conveyor practice, best quiz score), full attempt history with step-by-step details, and account actions: rename, reset password, disable/enable login, delete. |
| **Certificates** | Every certificate earned and its sync status, exportable with a public verification URL. New QR codes open the public verifier directly; the legacy paste checker remains available. |
| **Admins** | Who can sign in to the dashboard; give or remove admin access. |

**Certified** means the same as in the app: the training's practical part was passed **and** its quiz was passed afterwards. (Fire Safety: the fire scenarios, then the fire quiz. Machine Training: all 7 conveyor controls practised, then the machine quiz.)

## One-time Supabase setup

1. Create a project at supabase.com.
2. Open **SQL Editor** and run, in order:
   - `supabase/migrations/0001_init.sql`
   - `supabase/migrations/0002_admin_dashboard.sql` (adds bulk add, rename, delete and admin management; safe to run again)
   - `supabase/migrations/0003_certificates.sql` (adds server-backed issuance and public QR verification; safe to run again)
   - `supabase/migrations/0004_worker_certificates.sql` (lets signed-in workers retrieve their own issued certificates in the Unity app; safe to run again)
   - `supabase/migrations/0005_certificates_practice_score.sql` (accepts a certificate when the practical scored at least the pass mark, even if a PASS step was imperfect; safe to run again)
3. Go to **Authentication → Users → Add user**, create an admin with an email and password, and copy that user's UUID.
4. In the SQL Editor, run:
   ```sql
   insert into public.admins (user_id) values ('<admin-user-uuid>');
   ```
   Further admins can then be added from the dashboard's **Admins** page (create their account in Supabase first).
5. Go to **Authentication → Providers → Email** and turn off "Allow new users to sign up", so that only admins you add can sign in.
6. Copy **Project URL** and the **anon public key** from **Project Settings → API** into:
   - `dashboard/config.js`
   - `Assets/Resources/SupabaseConfig.asset` in Unity (select the asset and fill in the Inspector)

   The anon key is meant to be public: every table has row-level security, and all admin actions check the `admins` table on the server. **Never** put the `service_role` key in either file.

Already running the dashboard from before? Run any migration you have not applied yet, in number order. Existing workers and attempts are preserved.

## Running

- Local: from this folder, run `npx serve .` or `python -m http.server 8080`, then open http://localhost:8080. (Use `http://localhost`, not a `file://` path: the certificate checker needs a secure context.)
- GitHub Pages: `.github/workflows/dashboard-pages.yml` publishes this folder whenever `dashboard/` changes on `main`.
  1. On GitHub, open the repo's **Settings → Pages** and set **Source** to **GitHub Actions**.
  2. Push to `main`, or run the workflow by hand from the **Actions** tab.
  3. The site is published at `https://<user>.github.io/<repo>/`.

  The site is public, but only admins can sign in and see data.

## Use

- **+ Add workers**: enter one name per line. Each person gets a worker ID (e.g. `W-04821`) and an 8-character password. Passwords are shown **only once**, so copy or print the sheet before closing it.
- **Reset password** issues a new password. The old password stops working. Devices that already cached it have to log in online once, and their unsynced progress uploads after that.
- **Disable login** blocks the worker online, and blocks further syncing from their devices. Progress that was already synced is kept.
- **Delete** permanently removes the worker **and all their synced training records** (you type the worker ID to confirm). To keep the records, disable the login instead.
- **Export CSV** (Workers, Certificates, and each worker) downloads UTF-8 files that open correctly in Excel, including Hindi and Santali names.

## Verifying certificates

New certificates encode `https://namannpatel.github.io/AbhyasAR/#/verify/<certificate-id>`. A normal phone camera opens this route without an admin login. The route calls the public `verify_certificate` RPC, which returns only the trainee name, training, score, issue time and certificate ID for a real server-backed issuance. Worker codes, attempts and login details remain private.

The Unity app stores a certificate locally first, just like progress. `SyncService` uploads the passed attempts and then the issuance. Until that sync succeeds, the public page reports that the certificate has not reached the server yet.
The app's **View Certificate** section shows locally saved certificates offline, refreshes issued certificates from Supabase when online, and shows the 80% requirement for training without a certificate.

Legacy QR payloads using `AR-CERT|v1|name|training|score|time|checksum` can still be pasted into the admin checker. The dashboard then compares them with synced training records.

- **✓ Verified against training records**: genuine.
- **⚠ Not confirmed**: the checksum is fine but there's no matching record. The worker may have trained offline and not synced yet; otherwise treat it as not verified.
- **✗ Invalid**: altered, or not made by the app.

## How offline works (app side)

- The first login on a device needs internet. After that, the device keeps a salted hash of the password and logs in offline.
- Every completed attempt is saved on the device first, then uploaded whenever the device is online. The app retries every 30 s, backing off to every 5 min. Uploads are idempotent, so the same attempt is never counted twice.
- Fire scenarios are recorded as `FireTraining/<class>/<scenario>` (e.g. `FireTraining/BC/furnace`); records from older app builds (`FireTraining/BC`) are shown as "older app version". Quizzes are recorded as `…/Quiz`, the conveyor practice as `ConveyorTest/Practice`.
