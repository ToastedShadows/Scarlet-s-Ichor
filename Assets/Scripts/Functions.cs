using UnityEngine;
using UnityEngine.SceneManagement;

public class Functions : MonoBehaviour
{
    public void GoToSampleScene()
    {
        SceneManager.LoadScene("SampleScene");
    }
}