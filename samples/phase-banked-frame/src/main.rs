// Candidate build: the same hot frame loop as the control, preceded by bulky one-shot
// level preparation. The cold init phase pushes the movable program past fixed PRG, so the
// normal final-link ladder selects `nes-mmc3-tvrom-codebank-v1` and the phase placement
// policy has to keep `program:main:frame` whole inside one R6 bank.

class Preparation
{
    u8 slot;
    u8 checksum;

    inline void Fold8()
    {
        checksum += slot;
        checksum ^= 0x5A;
        slot += 1;
        checksum += slot;
        checksum ^= 0xA5;
        slot += 1;
        checksum += slot;
        checksum ^= 0x3C;
        slot += 1;
        checksum += slot;
        checksum ^= 0xC3;
        slot += 1;
    }

    inline void Fold64()
    {
        Fold8();
        Fold8();
        Fold8();
        Fold8();
        Fold8();
        Fold8();
        Fold8();
        Fold8();
    }

    inline void Fold256()
    {
        Fold64();
        Fold64();
        Fold64();
        Fold64();
    }
}

void PrepareLevel(Preparation preparation)
{
    preparation.Fold256();
    preparation.Fold256();
}

void Main()
{
    Scene scene;
    Preparation preparation;

    SetupScene();
    scene.Reset();
    PrepareLevel(preparation);

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
