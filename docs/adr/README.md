# قرارات المعمارية (ADRs)

## ADR-001 — Modular Monolith بـ Clean Architecture
**القرار:** deployable واحد (`MonitorAgent.Api`) وجواه موديولات تحت `src/Modules/<Name>/`، كل موديول فيه Domain وApplication وInfrastructure وقاعدة بيانات schema خاصة بيه. الـ host بيجمّع الموديولات وبس. الموديولات مش بتلمس بعض غير عن طريق contracts (أو أحداث integration)، واختبارات المعمارية بتطبّق ده على أي assembly اسمه `MonitorAgent.Modules.*` أول ما الـ host يضيفه.

## ADR-002 — CQRS بدون مكتبة خارجية
**القرار:** dispatcher داخلي في `MonitorAgent.BuildingBlocks.Application` بدل MediatR (أصبحت برخصة تجارية). الـ pipeline: Logging (الأبعد) ثم Validation بـ FluentValidation. الـ validation بترجع `Result` بـ `VALIDATION_FAILED` لو نوع النتيجة `Result`، وإلا بترمي `ValidationException` وبتتحول لـ 400. الـ handlers `internal`، وكل command أو query ليه handler واحد بالظبط (اختبار معماري + فحص عند التشغيل).

## ADR-003 — Angular workspace واحد بـ 3 تطبيقات
**القرار:** workspace واحد في `web/` بالـ Angular CLI: `site` (الموقع العام بـ SSR عشان الـ SEO)، و`portal` (بوابة العميل)، و`admin` (لوحة الأدمن)، ومكتبة `shared`. التطبيقات بتبدأ بالعربي (RTL) والإنجليزي هيتضاف مع i18n. Nx ممكن يتضاف فوق نفس الـ workspace (`nx init`) لو احتجنا caching أو affected builds لما الكود يكبر.

## ADR-004 — التكامل مع LicensingPlatform
**القرار:** LicensingPlatform نظام منفصل ومش بنعدّل فيه من هنا. الموقع بيتكلم معاه عن طريق الـ HTTP API بتاعه بس، من خلال adapter في موديول واحد (ACL)، ومفيش أي مشاركة لقاعدة البيانات. أي عقد بين الاتنين بيتكتب ويتختبر هنا بـ contract tests.
