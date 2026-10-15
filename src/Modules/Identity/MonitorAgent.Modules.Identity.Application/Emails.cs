using MonitorAgent.BuildingBlocks.Application;

namespace MonitorAgent.Modules.Identity.Application;

/// <summary>Account email texts, Arabic first then English.</summary>
internal static class Emails
{
    public static EmailMessage ConfirmEmail(string to, string name, string link) => new(to,
        "تأكيد البريد الإلكتروني | Confirm your email",
        $"""
        أهلاً {name}،
        لتأكيد بريدك الإلكتروني وتفعيل حسابك في Monitor Agent افتح الرابط ده:
        {link}
        لو مش انت اللي عملت الحساب تقدر تتجاهل الرسالة دي.

        Hello {name},
        Confirm your email to activate your Monitor Agent account:
        {link}
        If you didn't create this account, you can ignore this message.
        """);

    public static EmailMessage AlreadyRegistered(string to) => new(to,
        "محاولة تسجيل بحسابك | Sign-up attempt with your email",
        """
        حد حاول يعمل حساب جديد في Monitor Agent بالبريد ده، وانت عندك حساب بالفعل.
        لو نسيت كلمة السر استخدم "نسيت كلمة السر" من صفحة الدخول. لو مش انت، مفيش حاجة مطلوبة منك.

        Someone tried to create a Monitor Agent account with this email, but you already have one.
        If you forgot your password, use "Forgot password" on the sign-in page. If this wasn't you, no action is needed.
        """);

    public static EmailMessage ResetPassword(string to, string link) => new(to,
        "إعادة تعيين كلمة السر | Reset your password",
        $"""
        لإعادة تعيين كلمة السر افتح الرابط ده. الرابط صالح لمدة 3 ساعات ويستخدم مرة واحدة:
        {link}
        لو مش انت اللي طلبت ده تجاهل الرسالة، وكلمة السر هتفضل زي ما هي.

        To reset your password, open this link. It works once and expires in 3 hours:
        {link}
        If you didn't ask for this, ignore this message and your password stays the same.
        """);

    public static EmailMessage PasswordChanged(string to) => new(to,
        "تم تغيير كلمة السر | Your password was changed",
        """
        كلمة السر بتاعة حسابك في Monitor Agent اتغيرت، وكل الأجهزة التانية اتعملها خروج.
        لو مش انت اللي غيرتها، استخدم "نسيت كلمة السر" فوراً وكلم الدعم.

        Your Monitor Agent password was changed and other devices were signed out.
        If this wasn't you, use "Forgot password" right away and contact support.
        """);
}
