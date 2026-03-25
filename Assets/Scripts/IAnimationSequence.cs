using System;

/// <summary>
/// Contract that any animated Canvas must implement to communicate its completion
/// back to AnimationSequenceManager.
///
/// Attach a script implementing this interface to the root GameObject of your
/// animation Canvas, then drag the Canvas into the matching slot in AnimationSequenceManager.
///
/// The manager calls Play(onComplete) and waits for onComplete to be invoked.
/// Call onComplete at the very last frame of your sequence (after any fade-out).
///
/// Example minimal implementation:
/// <code>
/// public class MyAnimCanvas : MonoBehaviour, IAnimationSequence
/// {
///     public void Play(Action onComplete)
///     {
///         StartCoroutine(RunAnimation(onComplete));
///     }
///
///     private IEnumerator RunAnimation(Action onComplete)
///     {
///         // ... your animation steps ...
///         yield return new WaitForSeconds(3f);
///         onComplete?.Invoke();
///     }
/// }
/// </code>
/// </summary>
public interface IAnimationSequence
{
    /// <summary>
    /// Starts the animation sequence.
    /// Must call <paramref name="onComplete"/> exactly once when the sequence is fully finished,
    /// including any final fade-out. Do NOT call it before the last frame of the animation.
    /// </summary>
    /// <param name="onComplete">Callback invoked by AnimationSequenceManager to advance the game state.</param>
    void Play(Action onComplete);
}
