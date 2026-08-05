// Control build: the same hot frame loop as the candidate with no bulky cold preparation, so
// it links with fixed execution. It is the steady-state frame-work baseline the candidate's
// active-frame budget is derived from.

void Main()
{
    Scene scene;

    SetupScene();
    scene.Reset();

    while (true)
    {
        StepFrame(scene);
        if (scene.ReachedGoal())
        {
            break;
        }
    }

    CompleteLevel(scene);
}
