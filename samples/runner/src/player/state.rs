using Runner.Level;

class PlayerState
{
    Pixel x;
    Pixel y;
    i8 velocityY;
    bool grounded;
    Pixel displayFrame;
    bool displayFlipX;
    Pixel animTick;
    bool jumping;
    Pixel verticalSubpixel;

    inline void ApplyGravity()
    {
        if (!grounded)
        {
            if (jumping && Input.IsDown(Button.A) && velocityY < Jump.HeldGravityThreshold)
            {
                velocityY += Jump.HeldGravity;
            }
            else
            {
                velocityY += Jump.ReleasedGravity;
                if (velocityY > Jump.TerminalVelocity)
                {
                    velocityY = Jump.TerminalVelocity;
                }
            }

            Pixel verticalMotion = verticalSubpixel + velocityY;
            while (verticalMotion < 0)
            {
                y -= 1;
                verticalMotion += Jump.Subpixel;
            }
            while (verticalMotion >= Jump.Subpixel)
            {
                y += 1;
                verticalMotion -= Jump.Subpixel;
            }
            verticalSubpixel = verticalMotion;
        }
    }

    void Land(Pixel targetY)
    {
        y = targetY;
        velocityY = 0;
        grounded = true;
        jumping = false;
        verticalSubpixel = 0;
    }

    inline void BounceDown()
    {
        velocityY = Jump.BounceVelocity;
        grounded = false;
        jumping = false;
        verticalSubpixel = 0;
    }

    inline void StartJump(u8 horizontalSpeed)
    {
        velocityY = Jump.StandingVelocity;
        if (horizontalSpeed > 0)
        {
            velocityY = Jump.WalkingVelocity;
        }
        if (horizontalSpeed > MotionSpeed.Walk)
        {
            velocityY = Jump.RunningVelocity;
        }
        if (horizontalSpeed >= MotionSpeed.RunMax)
        {
            velocityY = Jump.PSpeedVelocity;
        }
        grounded = false;
        jumping = true;
        verticalSubpixel = 0;
    }

    inline void SelectDisplayFrame(bool moving)
    {
        displayFrame = grounded switch
        {
            false => 4,
            _ => moving switch
            {
                false => 0,
                _ => Animation.Frame(run, animTick)
            }
        };
    }

    inline void HandleJumpInput(u8 horizontalSpeed)
    {
        if (Input.WasPressed(Button.A) && grounded)
        {
            StartJump(horizontalSpeed);
            Sfx.Play(jump_sfx);
        }

        if (jumping && Input.WasReleased(Button.A))
        {
            jumping = false;
        }
    }

    inline void UpdateRunAnimation(CameraState view)
    {
        if (view.moving)
        {
            animTick += view.speed;
            if (animTick >= RunAnimation.CycleTicks)
            {
                animTick -= RunAnimation.CycleTicks;
            }
        }
        else
        {
            animTick = 0;
        }

        SelectDisplayFrame(view.moving);
    }
}
