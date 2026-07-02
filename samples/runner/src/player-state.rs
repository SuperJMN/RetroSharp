class PlayerState {
    Pixel x;
    Pixel y;
    i8 velocityY;
    bool grounded;
    Pixel displayFrame;
    bool displayFlipX;
    Pixel animTick;
    bool jumping;
    Pixel jumpTicks;
    Pixel gravityTick;

    inline void Reset(CameraState view) {
        x = view.x + Player.StartX;
        y = view.y + Player.StartY;
        velocityY = 0;
        grounded = true;
        displayFrame = 0;
        jumping = false;
        jumpTicks = 0;
        gravityTick = 0;
    }

    inline void ApplyGravity() {
        gravityTick++;
        if (gravityTick >= Jump.GravityFrames) {
            gravityTick = 0;
            velocityY += 1;
        }
        if (velocityY != 0) {
            grounded = false;
            y += velocityY;
        }
        if (velocityY < 0) {
            if (y > Player.TopWrapY) {
                y = 0;
                velocityY = 0;
                jumping = false;
            }
        }
    }

    inline void Land(Pixel targetY) {
        y = targetY;
        velocityY = 0;
        grounded = true;
        jumping = false;
        gravityTick = 0;
    }

    inline void BounceDown() {
        velocityY = Jump.BounceVelocity;
        grounded = false;
        jumping = false;
        gravityTick = 0;
    }

    inline void StartJump() {
        velocityY = Jump.Velocity;
        grounded = false;
        jumping = true;
        gravityTick = 0;
    }

    inline void SelectDisplayFrame(bool moving) {
        displayFrame = grounded switch {
            false => 4,
            _ => moving switch {
                false => 0,
                _ => Animation.Frame(run, animTick)
            }
        };
    }

    inline void HandleJumpInput() {
        if (Input.WasPressed(Button.A)) {
            if (grounded) {
                StartJump();
            }
        }

        if (jumping) {
            jumpTicks = Input.HoldTicks(Button.A);
            if (Input.IsDown(Button.A)) {
                if (jumpTicks < Jump.BoostTicks) {
                    if ((jumpTicks & Jump.BoostTickMask) != 0) {
                        velocityY -= 1;
                    }
                }
            }

            if (Input.WasReleased(Button.A)) {
                jumping = false;
            }
        }
    }

    inline void UpdateRunAnimation(CameraState view) {
        if (view.moving) {
            animTick += view.speed;
            if (animTick >= RunAnimation.CycleTicks) {
                animTick -= RunAnimation.CycleTicks;
            }
        } else {
            animTick = 0;
        }

        SelectDisplayFrame(view.moving);
    }
}
