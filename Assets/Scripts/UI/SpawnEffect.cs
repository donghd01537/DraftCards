using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DraftCards.UI
{
    public class SpawnEffect : MonoBehaviour
    {
        public static SpawnEffect Play(RectTransform parent, Vector2 anchoredPos, string text, Sprite smokeSprite, Color textColor)
        {
            if (parent == null) return null;

            GameObject go = new("SpawnFx", typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(120, 120);
            rect.anchoredPosition = anchoredPos;

            SpawnEffect fx = go.AddComponent<SpawnEffect>();
            fx._smokeSprite = smokeSprite;
            fx._text = text;
            fx._textColor = textColor;
            return fx;
        }

        private Sprite _smokeSprite;
        private string _text;
        private Color _textColor;

        private void Start()
        {
            StartCoroutine(Animate());
        }

        private IEnumerator Animate()
        {
            Image smoke = null;
            if (_smokeSprite != null)
            {
                GameObject sg = new("Smoke", typeof(RectTransform));
                RectTransform sr = sg.GetComponent<RectTransform>();
                sr.SetParent(transform, false);
                sr.anchorMin = new Vector2(0.5f, 0.5f);
                sr.anchorMax = new Vector2(0.5f, 0.5f);
                sr.pivot = new Vector2(0.5f, 0.5f);
                sr.sizeDelta = new Vector2(90, 35);
                sg.transform.localScale = Vector3.one * 0.4f;
                smoke = sg.AddComponent<Image>();
                smoke.sprite = _smokeSprite;
                smoke.preserveAspect = true;
                smoke.raycastTarget = false;
                smoke.color = new Color(1f, 1f, 1f, 0.85f);
            }

            TMP_Text txt = null;
            if (!string.IsNullOrEmpty(_text))
            {
                GameObject tg = new("FloatText", typeof(RectTransform));
                RectTransform tr = tg.GetComponent<RectTransform>();
                tr.SetParent(transform, false);
                tr.anchorMin = new Vector2(0.5f, 0.5f);
                tr.anchorMax = new Vector2(0.5f, 0.5f);
                tr.pivot = new Vector2(0.5f, 0.5f);
                tr.sizeDelta = new Vector2(180, 70);
                tr.anchoredPosition = new Vector2(0f, 25f);
                txt = tg.AddComponent<TextMeshProUGUI>();
                txt.text = _text;
                txt.fontSize = 56;
                txt.fontStyle = FontStyles.Bold;
                txt.color = _textColor;
                txt.alignment = TextAlignmentOptions.Center;
                txt.raycastTarget = false;
                txt.outlineWidth = 0.2f;
                txt.outlineColor = new Color32(0, 0, 0, 200);
            }

            const float duration = 0.7f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / duration);

                if (smoke != null)
                {
                    smoke.transform.localScale = Vector3.one * Mathf.Lerp(0.4f, 1.7f, u);
                    Color c = smoke.color;
                    c.a = Mathf.Lerp(0.85f, 0f, u);
                    smoke.color = c;
                }

                if (txt != null)
                {
                    RectTransform tr = (RectTransform)txt.transform;
                    Vector2 pos = tr.anchoredPosition;
                    pos.y = 25f + u * 90f;
                    tr.anchoredPosition = pos;
                    Color c = txt.color;
                    c.a = Mathf.Lerp(1f, 0f, u * u);
                    txt.color = c;
                }

                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
