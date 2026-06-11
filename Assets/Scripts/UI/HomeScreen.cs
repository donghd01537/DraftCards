using UnityEngine;
using UnityEngine.SceneManagement;

namespace DraftCards.UI
{
    public class HomeScreen : MonoBehaviour
    {
        [SerializeField] private string _battleSceneName = "BattlePrototype";

        public void StartGame()
        {
            SceneManager.LoadScene(_battleSceneName);
        }
    }
}
