# Analysis Modes Guide 📊

DeerBalak AIService supports **3 analysis modes** for different use cases. Switch between them instantly via `appsettings.json`.

---

## 🎭 **MOCK Mode** (Portfolio Demo)

**Perfect for:** Video demos, portfolios, quick testing

| Aspect | Detail |
|--------|--------|
| **Speed** | ⚡ Instant (no API calls) |
| **Dependencies** | ❌ None |
| **Accuracy** | 🎬 Fixed demo data |
| **Cost** | 💰 Free |
| **Configuration** | `"Mode": "MOCK"` |

### How It Works
- Always returns instant demo results
- Results vary by keyword:
  - **"danger", "urgent", "evacuate"** → `HIGH RISK` (score: 8)
  - **"road", "closed", "traffic"** → `MEDIUM RISK` (score: 5)  
  - **Default** → `SAFE` (score: 2)
- Perfect for showing UI without API delays

### Example Output (Danger Keywords)
```json
{
  "risk_score": 8,
  "risk_level": "HIGH RISK",
  "confidence": 92,
  "mode": "MOCK",
  "recommended_action": "DO_NOT_SHARE",
  "why_flagged": "[DEMO] Urgent/danger keywords detected"
}
```

---

## ⚙️ **FALLBACK Mode** (Local Intelligence)

**Perfect for:** Offline use, privacy-first, local testing

| Aspect | Detail |
|--------|--------|
| **Speed** | 🚀 Fast (no network latency) |
| **Dependencies** | ✅ Local only (Claim Tracking Service) |
| **Accuracy** | 📊 Rule-based + tracking |
| **Cost** | 💰 Free |
| **Configuration** | `"Mode": "FALLBACK"` |

### How It Works
- Uses **keyword detection** + **claim tracking**
- Analyzes posts for signals:
  - Urgency words (urgent, immediately, evacuate)
  - Fear language (danger, unsafe, threat)
  - Uncertainty (i heard, maybe, not sure)
  - Exaggeration (everyone, everywhere, nothing is safe)
- Tracks claim spread across similar posts
- Hybrid scoring: base score + tracking adjustments

### Example Analysis Flow
1. Detect urgency flag → +3 points
2. Check claim tracking → +2 points for high spread
3. Verify source credibility → adjust by ±1 point
4. Return final score (0-10)

---

## 🤖 **AI Mode** (Full Power)

**Perfect for:** Production, maximum accuracy, real-world scenarios

| Aspect | Detail |
|--------|--------|
| **Speed** | 🌐 ~2-5 seconds (API latency) |
| **Dependencies** | ✅ OpenAI API (requires key) |
| **Accuracy** | 🎯 Best (AI + hybrid fallback) |
| **Cost** | 💵 ~$0.001-0.005 per request |
| **Configuration** | `"Mode": "AI"` + valid `ApiKey` |

### How It Works
- Sends text to OpenAI GPT-4o-mini
- OpenAI returns analysis with:
  - Risk score (0-10)
  - Confidence (0-100)
  - Main signals
  - Detailed explanation
- **Hybrid approach**: 
  - Fallback analysis: 60% weight
  - AI analysis: 40% weight
  - Final score = weighted merge
- Better robustness: if AI fails, falls back automatically

### Example Output (AI Mode)
```json
{
  "risk_score": 6,
  "risk_level": "MEDIUM RISK",
  "confidence": 82,
  "mode": "AI-HYBRID",
  "recommended_action": "VERIFY_FIRST",
  "explanation": "Hybrid risk score blending AI and fallback analysis.",
  "signals": {
    "urgency": true,
    "fear": false,
    "uncertainty": true
  }
}
```

---

## ⚡ Quick Switch Guide

### For Portfolio Video
```json
{
  "Analysis": {
    "Mode": "MOCK"
  }
}
```
✅ No setup needed  
✅ Instant results  
✅ Perfect for demo video  

### For Local Development
```json
{
  "Analysis": {
    "Mode": "FALLBACK"
  }
}
```
✅ Works offline  
✅ Fast processing  
✅ No API costs  

### For Production
```json
{
  "Analysis": {
    "Mode": "AI"
  },
  "OpenAI": {
    "Enabled": true,
    "ApiKey": "sk-..."  // or use OPENAI_API_KEY env var
  }
}
```
✅ Best accuracy  
✅ Robust with fallback  
✅ Small per-request cost  

---

## 🔍 Console Output Indicators

Watch the console to see which mode is active:

| Output | Meaning |
|--------|---------|
| `🎭 Running in MOCK mode` | Demo data mode active |
| `⚙️ Using fallback analysis` | Local analysis mode active |
| `✅ OpenAI API service initialized` | AI mode active with API key |
| `📋 Returning cached result` | Result from cache (any mode) |
| `📊 Analysis mode set to: MOCK` | Configuration log at startup |

---

## 📋 Comparison Table

| Feature | MOCK | FALLBACK | AI |
|---------|------|----------|-----|
| **Setup Time** | <1 min | <1 min | 5-10 min (API key) |
| **Speed** | ⚡ Instant | 🚀 Fast | 🌐 Moderate |
| **Accuracy** | 🎬 Fixed | 📊 Good | 🎯 Best |
| **Dependencies** | None | Local | OpenAI API |
| **Cost** | Free | Free | ~$0.001/request |
| **Best For** | Demo/Video | Local Dev | Production |
| **Offline** | ✅ Yes | ✅ Yes | ❌ No |

---

## 🚀 Recommendation

- **Start with MOCK** for portfolio video (fast, no setup)
- **Switch to FALLBACK** for testing features locally
- **Use AI** when deploying to production for best results

All three modes return **identical JSON schema**, so switching is seamless!
