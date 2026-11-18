# MG Business Web

نسخه وب مستقل MG Business؛ یک محصول مرورگرمحور و PWA برای مدیریت کسب‌وکارها. استفاده از آن به نصب یا روشن بودن برنامه ویندوز وابسته نخواهد بود.

## هدف محصول
مدیریت عملیات روزمره کسب‌وکار از موبایل، تبلت و دسکتاپ: داشبورد، فروش و POS، سفارش‌ها، کالا و انبار، مشتریان و تأمین‌کنندگان، حسابداری، کارکنان، گزارش‌ها، تنظیمات، فروشگاه عمومی مشتریان و ماژول‌های تخصصی هر صنعت.

## اجرای نسخه فعلی
```bash
cd web
npm install
npm run dev
npm test
npm run build
```

## وضعیت فعلی
نسخه موجود یک prototype تعاملی React/Vite است و داده‌های نمایشی را در localStorage نگه می‌دارد. این داده‌ها سروری، مشترک بین کاربران یا مناسب عملیات واقعی نیستند. تا زمان تکمیل API، احراز هویت و پایگاه داده، از آن برای ثبت فروش، اطلاعات واقعی مشتریان یا حسابداری استفاده نکنید.

## معماری هدف
- Frontend: React + TypeScript + Vite، PWA، طراحی واکنش‌گرا، فارسی RTL و انگلیسی LTR.
- Backend: ASP.NET Core Web API و worker به شکل modular monolith.
- Database: PostgreSQL مرجع اصلی داده‌ها.
- Identity: نشست امن، مدیریت نقش و مجوز، MFA-ready.
- Multi-tenancy: جداسازی اجباری داده‌های کسب‌وکار و شعب در سمت سرور.
- Deployment: کانتینری، TLS، مدیریت اسرار و پشتیبان‌گیری قابل بازیابی.
- Integrations: درگاه پرداخت، پیامک/ایمیل، چاپ و سخت‌افزار از طریق adapter امن.

## مستندات معماری
- [Product Blueprint](docs/PRODUCT_BLUEPRINT.md)
- [Initial API Contract](docs/API_CONTRACT.md)

GitHub Pages فقط برای preview استاتیک رابط کاربری است؛ محیط production باید API، worker و PostgreSQL داشته باشد و روی میزبانی مستقل اجرا شود.
