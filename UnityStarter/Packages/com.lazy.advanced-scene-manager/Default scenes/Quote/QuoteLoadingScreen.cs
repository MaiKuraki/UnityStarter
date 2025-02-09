using System.Collections;
using AdvancedSceneManager.Utility;
using UnityEngine;
using UnityEngine.UI;

namespace AdvancedSceneManager.Defaults
{

    /// <summary>A default loading screen script. Displays quotes.</summary>
    public class QuoteLoadingScreen : ProgressBarLoadingScreen
    {

        public Quotes quotes;
        public Text QuoteText;
        public Text QuoteCountText;
        public Text pressAnyKeyToContinueText;
        public RectTransform Content;
        public RectTransform Text;

        public float slideshowDelay = 4f;
        int index = -1;

        public override IEnumerator OnOpen()
        {

            Next();
            yield return FadeIn();
            StartCoroutine(Slideshow());

        }

        public override IEnumerator OnClose()
        {

            pressAnyKeyToContinueText.CrossFadeAlpha(1, 0.5f, true);
            yield return WaitForAnyKey();

            yield return Content.Fade(0, 0.5f, true);
            yield return FadeOut();

        }

        IEnumerator Slideshow()
        {
            while (this)
            {
                yield return new WaitForSecondsRealtime(slideshowDelay);
                yield return Text.Fade(0, 0.5f, true);

                yield return new WaitForSeconds(1);
                Next();

                yield return Text.Fade(1, 0.5f, true);
            }
        }

        void Next()
        {
            index = (int)Mathf.Repeat(index + 1, quotes.quoteList.Count);
            var quote = quotes.quoteList[index];
            QuoteText.text = quote;

            QuoteCountText.text = $"{index + 1} / {quotes.quoteList.Count}";
        }

    }

}