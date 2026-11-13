import android.content.Context;
import android.speech.tts.TextToSpeech;
import java.util.Locale;

public class UnityTTSBridge {
    private static TextToSpeech tts;
    private static boolean ready = false;

    public static void init(Context context) {
        if (context == null) return;
        if (tts != null) return;

        tts = new TextToSpeech(context.getApplicationContext(), new TextToSpeech.OnInitListener() {
            @Override
            public void onInit(int status) {
                ready = (status == TextToSpeech.SUCCESS);
                if (!ready) return;

                Locale zhCN = Locale.SIMPLIFIED_CHINESE;
                int r = tts.setLanguage(zhCN);
                if (r == TextToSpeech.LANG_MISSING_DATA || r == TextToSpeech.LANG_NOT_SUPPORTED) {
                    tts.setLanguage(Locale.CHINESE);
                }
                tts.setSpeechRate(1.0f);
                tts.setPitch(1.0f);
            }
        });
    }

    public static void speak(String text) {
        if (!ready || tts == null) return;
        if (text == null || text.trim().isEmpty()) return;
        tts.speak(text, TextToSpeech.QUEUE_FLUSH, null, "ARAnimalTTS");
    }

    public static void stop() {
        if (tts != null) tts.stop();
    }

    public static void shutdown() {
        if (tts != null) {
            tts.stop();
            tts.shutdown();
            tts = null;
        }
        ready = false;
    }
}
