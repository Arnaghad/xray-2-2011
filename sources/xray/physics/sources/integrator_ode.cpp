////////////////////////////////////////////////////////////////////////////
//	Created 	: 11.02.2008
//	Author		: Konstantin Slipchenko
//	Description : integrator
////////////////////////////////////////////////////////////////////////////
#include "pch.h"

#include "integrator_ode.h"
#include "ode_include.h"

void integrator_ode::step(float time_delta) {
  if (!m_bodies.empty()) {
    dInternalStepIsland(0, m_bodies.data(), m_bodies.size(),
                        m_joints.empty() ? nullptr : m_joints.data(),
                        m_joints.size(), time_delta);
  }

  m_bodies.clear();
  m_joints.clear();
}
