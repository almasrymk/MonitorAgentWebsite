# Monitor Agent Website

موقع Monitor Agent التجاري: الموقع العام، وبوابة العميل، ولوحة الأدمن. المشروع منفصل عن LicensingPlatform، وبيتكامل معاه عن طريق الـ API بتاعه بس (ADR-004).

## التقنيات

- **الباك إند:** .NET 10، ASP.NET Core، SQL Server، Clean Architecture على شكل Modular Monolith مع DDD و CQRS.
- **الواجهات:** Angular 21، وفيها 3 تطبيقات (الموقع العام بـ SSR، وبوابة العميل، ولوحة الأدمن) ومكتبة مشتركة.
- **الاختبارات:** xUnit و NetArchTest و WebApplicationFactory للباك إند، و Vitest للواجهات.

## التشغيل المحلي

المتطلبات: .NET SDK 10، و Node 22، و SQL Server (محلي أو عن طريق Docker).

```bash
# الباك إند
dotnet test
dotnet run --project src/Host/MonitorAgent.Api      # http://localhost:5200/health/live

# الواجهات
cd web
npm ci
npx ng serve site       # http://localhost:4200
npx ng serve portal
npx ng serve admin
```

بـ Docker: انسخ `.env.example` لـ `.env` واكتب باسورد قوي، وبعدين `docker compose up --build`.

## الهيكل

```
src/
  BuildingBlocks/
    MonitorAgent.SharedKernel                 Entity, AggregateRoot, ValueObject, Result, DomainEvent
    MonitorAgent.BuildingBlocks.Application   CQRS داخلي: ICommand/IQuery، IDispatcher، Logging وValidation (ADR-002)
  Host/MonitorAgent.Api                       الـ host الوحيد: ProblemDetails، Health، CORS، Rate limiting، Security headers
  Modules/                                    كل موديول هنا بالـ Domain/Application/Infrastructure بتوعه (ADR-001)
tests/MonitorAgent.Tests                      Unit وArchitecture وAPI
web/                                          Angular workspace: projects/site، portal، admin، shared (ADR-003)
docs/adr/                                     قرارات المعمارية
```

## الأمان

- الـ API بيرجّع security headers في كل رد، ومفيش أي تفاصيل داخلية بتطلع في الأخطاء.
- الـ CORS مقفول على الـ origins المكتوبة في الإعدادات بس، وفي rate limiting لكل IP.
- الـ CI فيه CodeQL، وفحص الأسرار بـ gitleaks، وفحص ثغرات NuGet و npm بيوقّف الـ build.
- مفيش أسرار في الريبو. الإعدادات الحساسة بتيجي من environment variables أو user-secrets.
