# النشر

الموقع بيتنشر كـ 3 صور docker ورا Caddy على دومين واحد (ADR-019). الخطوات دي لسيرفر Linux واحد. على Azure نفس الصور ونفس أسماء الإعدادات.

## قبل أول نشر
1. سيرفر فيه Docker و Docker Compose، والبورتات 80 و443 مفتوحة.
2. الدومين (A record) بيشاور على IP السيرفر. Caddy محتاج ده عشان ياخد الشهادة.
3. قاعدة بيانات SQL Server: Azure SQL أو SQL Server مُدار (المقترح)، أو على نفس السيرفر بـ `--profile db`. الموقع بيعمل الجداول لوحده أول ما يشتغل (Migrate).
4. مستخدم لقاعدة البيانات خاص بالموقع، مش `sa`.

## أول نشر
```bash
git clone https://github.com/almasrymk/MonitorAgentWebsite.git && cd MonitorAgentWebsite/deploy
cp env.example .env && chmod 600 .env   # واملا القيم
docker compose up -d --build            # أو: docker compose --profile db up -d --build
```
بعدها:
- `https://<الدومين>/health/ready` لازم يرجّع 200.
- ادخل `/admin/` بالأدمن اللي في `Identity__BootstrapAdmin*`، وفعّل التحقق بخطوتين، وبعدين امسح السطرين دول من `.env` واعمل `docker compose up -d`.
- شغّل `scripts/ci/stack-smoke.sh` بعد ما تغيّر `base` للدومين.

## تحديث
```bash
git pull && docker compose up -d --build
```
الـ API بيطبّق أي migration جديد وهو بيقوم. خد نسخة احتياطية قبل أي تحديث فيه migration ([backup-restore.md](backup-restore.md)).

## الرجوع لنسخة قبلها
```bash
git checkout <tag اللي قبله> && docker compose up -d --build
```
لو التحديث كان فيه migration، ارجع لنسخة قاعدة البيانات اللي اتاخدت قبله.

## حاجات تتراجع
- `docker compose logs -f api` للـ logs، و`traceId` اللي في رسالة الخطأ بيوصلك للسطر.
- الشهادة: Caddy بيجدّدها لوحده. لو فشل كذا مرة، بيبعت على `ACME_EMAIL`.
- `.env` فيه كل الأسرار: صلاحيات 600، وخارج أي نسخة احتياطية مش مشفّرة.
