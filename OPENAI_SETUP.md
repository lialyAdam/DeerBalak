# 🔐 إعداد OpenAI API Key بأمان

## 🚨 تحذير أمني مهم!

**لا تضع مفتاح API في أي ملف في المشروع!**

## ✅ الطريقة الصحيحة:

### 1. احصل على مفتاح جديد
- اذهب إلى: https://platform.openai.com/api-keys
- احذف المفتاح القديم إذا كان موجوداً
- أنشئ مفتاح جديد

### 2. احفظ المفتاح كمتغير بيئة

#### في Windows PowerShell:
```powershell
# استبدل YOUR_NEW_API_KEY_HERE بالمفتاح الجديد
[System.Environment]::SetEnvironmentVariable('OpenAI__ApiKey', 'YOUR_NEW_API_KEY_HERE', 'User')
```

#### أو في Command Prompt:
```cmd
setx OpenAI__ApiKey "YOUR_NEW_API_KEY_HERE"
```

### 3. أعد تشغيل Visual Studio / Terminal

### 4. اختبر النظام
أنشئ منشور مثل:
```
URGENT: Everyone must evacuate immediately! There is danger everywhere!
```

يجب أن تشاهد:
- ✅ Risk Score عالي (4-6+)
- ✅ Confidence 60-80%
- ✅ شرح من AI وليس fallback

## 🔍 استكشاف الأخطاء

### إذا ظهر: "HTTP 401 Unauthorized"
- المفتاح غير صحيح أو منتهي الصلاحية

### إذا ظهر: "HTTP 429 Too Many Requests"
- تجاوزت الحد المسموح (quota exceeded)

### إذا ظهر: "AI analysis unavailable"
- المفتاح غير موجود في متغيرات البيئة

## 📁 ملفات الإعداد

- ✅ `.env` - ملف آمن (مضاف لـ .gitignore)
- ✅ `appsettings.Development.json` - بدون مفتاح
- ✅ متغيرات البيئة - الطريقة الأكثر أماناً