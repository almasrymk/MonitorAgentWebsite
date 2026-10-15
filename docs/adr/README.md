# قرارات المعمارية (ADRs)

## ADR-001 — Modular Monolith بـ Clean Architecture
**القرار:** deployable واحد (`MonitorAgent.Api`) وجواه موديولات تحت `src/Modules/<Name>/`، كل موديول فيه Domain وApplication وInfrastructure وقاعدة بيانات schema خاصة بيه. الـ host بيجمّع الموديولات وبس. الموديولات مش بتلمس بعض غير عن طريق contracts (أو أحداث integration)، واختبارات المعمارية بتطبّق ده على أي assembly اسمه `MonitorAgent.Modules.*` أول ما الـ host يضيفه.

## ADR-002 — CQRS بدون مكتبة خارجية
**القرار:** dispatcher داخلي في `MonitorAgent.BuildingBlocks.Application` بدل MediatR (أصبحت برخصة تجارية). الـ pipeline: Logging (الأبعد) ثم Validation بـ FluentValidation. الـ validation بترجع `Result` بـ `VALIDATION_FAILED` لو نوع النتيجة `Result`، وإلا بترمي `ValidationException` وبتتحول لـ 400. الـ handlers `internal`، وكل command أو query ليه handler واحد بالظبط (اختبار معماري + فحص عند التشغيل).

## ADR-003 — Angular workspace واحد بـ 3 تطبيقات
**القرار:** workspace واحد في `web/` بالـ Angular CLI: `site` (الموقع العام بـ SSR عشان الـ SEO)، و`portal` (بوابة العميل)، و`admin` (لوحة الأدمن)، ومكتبة `shared`. التطبيقات بتبدأ بالعربي (RTL) والإنجليزي هيتضاف مع i18n. Nx ممكن يتضاف فوق نفس الـ workspace (`nx init`) لو احتجنا caching أو affected builds لما الكود يكبر.

## ADR-004 — التكامل مع LicensingPlatform
**القرار:** LicensingPlatform نظام منفصل ومش بنعدّل فيه من هنا. الموقع بيتكلم معاه عن طريق الـ HTTP API بتاعه بس، من خلال adapter في موديول واحد (ACL)، ومفيش أي مشاركة لقاعدة البيانات. أي عقد بين الاتنين بيتكتب ويتختبر هنا بـ contract tests.

## ADR-005 — تسجيل الدخول والجلسات والـ MFA
**القرار:** ASP.NET Core Identity جوه موديول `Identity` بقاعدة بياناته في schema اسمها `identity`. الجلسة cookie من نوع `__Host-` (HttpOnly، Secure، SameSite=Strict) مش JWT، عشان التوكن ما يبقاش متاح لأي JavaScript.
- **CSRF:** أي request بيغيّر حاجة لازم يبعت الـ header `X-XSRF-TOKEN`. الواجهة بتجيب التوكن من `GET /api/v1/auth/csrf` لما تفتح، وتاني بعد الدخول أو الخروج لأن التوكن مربوط بالمستخدم.
- **الباسورد:** 12 حرف على الأقل من غير قواعد تركيب (NIST 800-63B)، والحساب بيتقفل 15 دقيقة بعد 5 محاولات غلط.
- **منع كشف الحسابات:** التسجيل ونسيت الباسورد بيرجعوا نفس الرد سواء الإيميل موجود أو لأ، والدخول بإيميل مش موجود بياخد نفس الوقت ونفس الخطأ زي الباسورد الغلط.
- **الـ MFA:** TOTP مع recovery codes. حسابات الموظفين (Admin وSupport وFinance والمحتوى) ما تقدرش تدخل لوحة الأدمن غير بجلسة اتعملت بالـ MFA، وما تقدرش تقفله.
- **الجلسات:** تغيير الباسورد أو إعادة تعيينه أو "اخرج من كل الأجهزة" بيقفل الجلسات التانية، لأن الـ security stamp بيتراجع كل `Identity:SessionValidationSeconds` (60 ثانية افتراضيًا).
- **مفاتيح التشفير** (Data Protection) متخزنة في نفس قاعدة البيانات، عشان أكتر من نسخة من الـ API تقدر تقرا نفس الـ cookies.

## ADR-006 — حساب الشركة هو الـ tenant
**القرار:** كل عميل عبارة عن شركة (`CustomerAccount` في موديول `Customers`)، وكل حاجة بتاعة العميل (التراخيص والأجهزة والفواتير في المراحل الجاية) بتتربط بالـ id بتاعها.
- الشخص الواحد عضو في شركة واحدة بس (unique index على `Members.UserId`).
- مفيش أي endpoint بياخد company id. الشركة دايمًا بتتجاب من عضوية المستخدم اللي داخل، فمستحيل عميل يوصل لبيانات عميل تاني حتى لو خمّن ids. أي id لعضو أو دعوة مش في شركته بيرجع NOT_FOUND.
- الأدوار: Owner (بيدير الفريق)، وBilling، وTechnical، وViewer. لازم يفضل Owner واحد على الأقل.
- الدعوة بتتبعت بالإيميل وصالحة 7 أيام ومرة واحدة، ولازم تتقبل من حساب بنفس الإيميل. التوكن مش بيتخزن، بيتخزن الـ SHA-256 بتاعه بس.
- الموديولات التانية بتعرف بيانات المستخدم (الإيميل والاسم) عن طريق `IUserDirectory` في `Identity.Contracts`، مش من قاعدة بيانات Identity مباشرة.

## ADR-007 — الربط مع LicensingPlatform بحساب خدمة
**القرار:** موديول `Licenses` هو طبقة الحماية (ACL) الوحيدة اللي بتكلم LicensingPlatform. بيدخل بمستخدم مخصص للموقع (`Licenses:Platform:ServiceEmail` و`ServicePassword` من الأسرار)، وبيستخدم الـ endpoints الموجودة من غير ما نعدّل حاجة في LicensingPlatform.
- **ليه:** الـ client tokens في LicensingPlatform بتوصل لـ endpoints الأجهزة بس، ومفيش API للأنظمة التانية. لما يتعمل integration API هناك، هنغيّر الـ client بس، والشاشات والـ handlers مش هتتأثر.
- **الصلاحيات المطلوبة لحساب الخدمة:** catalog.read و customers.manage و subscriptions.manage و licenses.read و licenses.manage.
- **منع التكرار:** كل طلب له `OrderReference` فريد في جدول `licenses.Grants`. إصدار الرخصة بيتم على خطوات (عميل، اشتراك، رخصة)، وكل خطوة بتتحفظ. فلو حصل خطأ في النص، إعادة المحاولة بنفس الرقم بتكمّل من مكانها ومش بتعمل اشتراك تاني.
- **مفتاح المنتج:** LicensingPlatform بيرجّعه مرة واحدة بس. بنخزّنه متشفّر بـ Data Protection، وبيظهر للـ Owner والـ Technical بس، وكل مرة بيتشاف بتتسجل.
- **العزل:** الرخصة بتتجاب من LicensingPlatform وبعدين نتأكد إن الـ CustomerId بتاعها هو عميل شركة المستخدم، وإلا نرجّع LICENSE_NOT_FOUND.
- **قائمة الأسعار:** الخطط المنشورة والمدفوعة والحالية بس، وبتتخزّن 5 دقايق. ولو LicensingPlatform وقع، بنعرض آخر نسخة.
- **مخاطرة معروفة:** لو LicensingPlatform أصدر الرخصة واحنا وقعنا قبل ما نحفظها، إعادة المحاولة هتصدر رخصة تانية على نفس الاشتراك. ده بيتقفل لما LicensingPlatform يقبل مفتاح idempotency.

## ADR-008 — الدفع بصفحة مزوّد الدفع وإشعارات موقّعة
**القرار:** موديول `Commerce` فيه الطلبات والدفع والفواتير والدفتر. العميل بيدفع في صفحة مزوّد الدفع (hosted checkout)، فبيانات الكارت عمرها ما بتعدّي على سيرفراتنا، وده بيخلّينا في أبسط مستوى من PCI DSS (SAQ A).
- **المزوّد:** واجهة `IPaymentGateway` (إنشاء checkout، وقراءة الإشعار والتحقق من توقيعه). دلوقتي فيه `sandbox` للتطوير والاختبارات بس، والموقع بيرفض يشتغل بيه برّه Development. Paymob أو Stripe بيتضافوا كـ adapter من غير ما حاجة تانية تتغيّر.
- **الإشعارات (webhooks):** `POST /api/v1/payments/notifications/{provider}` من غير session ولا CSRF. التوقيع بيتراجع الأول قبل أي حاجة، ولو غلط نرجّع 401.
- **منع التكرار:** كل إشعار بيتسجل في `commerce.ProcessedNotifications` بمفتاح (المزوّد، رقم الحدث). الإشعار المكرر بياخد 200 ومش بيعمل حاجة. ولو اتنين وصلوا مع بعض، واحد بس بيتحفظ والتاني بياخد 503 فالمزوّد يبعته تاني. وفيه فاتورة واحدة بس لكل طلب (unique على OrderId)، وإصدار الرخصة نفسه idempotent برقم الطلب (ADR-007). يعني إشعار مكرر عمره ما يعمل رخصتين.
- **المبلغ:** الطلب بيتعلّم مدفوع بس لو المبلغ والعملة مطابقين بالظبط لسعر الطلب.
- **بعد الدفع:** سطر في الدفتر، وفاتورة برقم متسلسل من غير فراغات (`INV-yyyy-nnnnnn`)، وبعدين إصدار الرخصة. لو LicensingPlatform واقع، الطلب بيفضل `Paid` وفيه worker بيعيد المحاولة كل دقيقة (`Commerce:FulfillmentRetrySeconds`).
- **الصلاحيات:** الشراء للـ Owner والـ Billing بس. الأدمن يقدر يسجّل دفع يدوي (تحويل بنكي مثلًا) ويشوف كل الطلبات.
- **لسه:** الاسترداد (refund)، والتجديد، والمطابقة الليلية مع المزوّد، بعد اختيار المزوّد.
