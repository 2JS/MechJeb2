using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace MuMech {
    public abstract class PontryaginBase {
        public class Arc
        {
            public Stage stage;
            public double dV;  /* actual integrated time of the burn */

            public double isp { get { return stage.isp; } }
            public double thrust { get { return stage.thrust; } }
            public double m0 { get { return stage.m0; } }
            public double max_bt { get { return stage.max_bt; } }
            public double c { get { return stage.c; } }
            public double max_bt_bar { get { return stage.max_bt_bar; } }

            public bool infinite;  /* zero mdot, infinite burntime+isp */

            public override string ToString()
            {
                return "stage: " + stage;
            }

            public Arc(Stage stage)
            {
                this.stage = stage;
            }
        }

        /* these objects track KSP stages */
        public class Stage
        {
            public double isp, thrust, m0, max_bt;
            public double c, max_bt_bar;
            public int ksp_stage;
            public bool staged = false;  /* if this stage has been jettisoned */

            public override string ToString()
            {
                return "ksp_stage: "+ ksp_stage + " isp:" + isp + " thrust:" + thrust + " m0: " + m0 + " maxt:" + max_bt + " maxtbar: " + max_bt_bar + " c: " + c;
            }

            public Stage(PontryaginBase p, double m0, double isp = 0, double thrust = 0, double max_bt = 0, int ksp_stage = -1)
            {
                UpdateStage(p, m0, isp, thrust, max_bt, ksp_stage);
            }

            public void UpdateStage(PontryaginBase p, double m0, double isp = 0, double thrust = 0, double max_bt = 0, int ksp_stage = -1)
            {
                this.isp = isp;
                this.thrust = thrust;
                this.m0 = m0;
                this.max_bt = max_bt;
                this.c = g0 * isp / Math.Sqrt( p.r_scale / p.g_bar );  /* FIXME: is this not just / v_scale ? */
                this.max_bt_bar = max_bt / p.t_scale;
                this.ksp_stage = ksp_stage;
            }
        }

        public class Solution
        {
            public double t0;  // kerbal time
            public double t_scale;
            public double v_scale;
            public double r_scale;

            public Solution(double t_scale, double v_scale, double r_scale, double t0)
            {
                this.t_scale = t_scale;
                this.v_scale = v_scale;
                this.r_scale = r_scale;
                this.t0 = t0;
            }

            public double tgo(double t)
            {
                double tbar = ( t - t0 ) / t_scale;
                return ( tmax() - tbar ) * t_scale;
            }

            public double vgo(double t)
            {
                return dV(tf()) - dV(t);
            }

            public double tf() // kerbal time
            {
                return t0 + tmax() * t_scale;
            }

            public double tmax() // normalized time
            {
                return segments[segments.Count-1].tmax;
            }

            public double tmin() // normalized time
            {
                return segments[0].tmin;
            }

            public Vector3d r(double t)
            {
                double tbar = ( t - t0 ) / t_scale;
                return Planetarium.fetch.rotation * new Vector3d( interpolate(0, tbar), interpolate(1, tbar), interpolate(2, tbar) ) * r_scale;
            }

            public Vector3d r_bar(double tbar)
            {
                return new Vector3d( interpolate(0, tbar), interpolate(1, tbar), interpolate(2, tbar) );
            }

            public Vector3d v(double t)
            {
                double tbar = ( t - t0 ) / t_scale;
                return Planetarium.fetch.rotation * new Vector3d( interpolate(3, tbar), interpolate(4, tbar), interpolate(5, tbar) ) * v_scale;
            }

            public Vector3d v_bar(double tbar)
            {
                return new Vector3d( interpolate(3, tbar), interpolate(4, tbar), interpolate(5, tbar) );
            }

            public Vector3d pv(double t)
            {
                double tbar = ( t - t0 ) / t_scale;
                return Planetarium.fetch.rotation * new Vector3d( interpolate(6, tbar), interpolate(7, tbar), interpolate(8, tbar) );
            }

            public Vector3d pv_bar(double tbar)
            {
                return new Vector3d( interpolate(6, tbar), interpolate(7, tbar), interpolate(8, tbar) );
            }

            public Vector3d pr(double t)
            {
                double tbar = ( t - t0 ) / t_scale;
                return Planetarium.fetch.rotation * new Vector3d( interpolate(9, tbar), interpolate(10, tbar), interpolate(11, tbar) );
            }

            public Vector3d pr_bar(double tbar)
            {
                return new Vector3d( interpolate(9, tbar), interpolate(10, tbar), interpolate(11, tbar) );
            }

            public double m(double t)
            {
                double tbar = ( t - t0 ) / t_scale;
                return interpolate(12, tbar);
            }

            public double m_bar(double tbar)
            {
                return interpolate(12, tbar);
            }

            public double dV(double t)
            {
                double tbar = ( t - t0 ) / t_scale;
                return interpolate(13, tbar) * v_scale;
            }

            public void pitch_and_heading(double t, ref double pitch, ref double heading)
            {
                double tbar = ( t - t0 ) / t_scale;
                Vector3d rbar = new Vector3d( interpolate(0, tbar), interpolate(1, tbar), interpolate(2, tbar) );
                Vector3d pv = new Vector3d( interpolate(6, tbar), interpolate(7, tbar), interpolate(8, tbar) );
                Vector3d headVec = pv - Vector3d.Dot(pv, rbar) * rbar;
                Vector3d east = Vector3d.Cross(rbar, new Vector3d(0, 1, 0)).normalized;
                Vector3d north = Vector3d.Cross(east, rbar).normalized;
                pitch = 90.0 - Vector3d.Angle(pv, rbar);
                heading = MuUtils.ClampDegrees360(UtilMath.Rad2Deg * Math.Atan2(Vector3d.Dot(headVec, east), Vector3d.Dot(headVec, north)));
            }

            double interpolate(int i, double tbar)
            {
                for(int k = 0; k < segments.Count; k++)
                {
                    Segment s = segments[k];
                    if (tbar < s.tmax)
                        return alglib.barycentriccalc(s.interpolant[i], tbar);
                }
                return alglib.barycentriccalc(segments[segments.Count-1].interpolant[i], tbar);
            }

            List<Segment> segments = new List<Segment>();

            class Segment
            {
                public Arc arc;
                public double tmin, tmax;
                public alglib.barycentricinterpolant[] interpolant = new alglib.barycentricinterpolant[14];

                public Segment(double[] t, double[,] y, Arc a)
                {
                    arc = a;
                    int n = t.Length;
                    tmin = t[0];
                    tmax = t[n-1];
                    for(int i = 0; i < 14; i++)
                    {
                        /*
                        double[] yi = new double[n];
                        for(int j = 0; j < n; j++)
                        {
                            yi[j] = y[j,i];
                        }
                        alglib.polynomialbuildeqdist(tmin, tmax, yi, out interpolant[i]);
                        */
                        double[] yi = new double[n-2];
                        for(int j = 1; j < (n-1); j++)
                        {
                            yi[n-j-2] = y[j,i];  // reverse the array and omit the endpoints for chebyshev interpolation
                        }
                        alglib.polynomialbuildcheb1(tmin, tmax, yi, out interpolant[i]);
                    }
                }
            }

            public void AddSegment(double[] t, double[,] y, Arc a)
            {
                segments.Add(new Segment(t, y, a));
            }
        }

        public List<Stage> stages = new List<Stage>();
        public List<Arc> last_arcs;
        public double mu;
        public Action<double[], double[]> bcfun;
        public const double g0 = 9.80665;
        public Vector3d r0, v0, r0_bar, v0_bar;
        public Vector3d pv0, pr0;
        public double tgo, tgo_bar, vgo, vgo_bar;
        public double g_bar, r_scale, v_scale, t_scale;  /* problem scaling */
        public double dV, dV_bar;  /* guess at dV */

        public PontryaginBase(double mu, Vector3d r0, Vector3d v0, Vector3d pv0, Vector3d pr0, double dV)
        {
            QuaternionD rot = Quaternion.Inverse(Planetarium.fetch.rotation);
            r0 = rot * r0;
            v0 = rot * v0;
            pv0 = rot * pv0;
            pr0 = rot * pr0;
            this.r0 = r0;
            this.v0 = v0;
            this.last_arcs = null;
            this.mu = mu;
            this.pv0 = pv0;
            this.pr0 = pr0;
            this.dV = dV;
            double r0m = this.r0.magnitude;
            g_bar = mu / ( r0m * r0m );
            r_scale = r0m;
            v_scale = Math.Sqrt( r0m * g_bar );
            t_scale = Math.Sqrt( r0m / g_bar );
            r0_bar = this.r0 / r_scale;
            v0_bar = this.v0 / v_scale;
            dV_bar = dV / v_scale;
        }

        public void UpdatePosition(Vector3d r0, Vector3d v0, Vector3d lambda, Vector3d lambdaDot, double tgo, double vgo)
        {
            if (thread != null && thread.IsAlive)
                return;

            QuaternionD rot = Quaternion.Inverse(Planetarium.fetch.rotation);
            r0 = rot * r0;
            v0 = rot * v0;
            this.r0 = r0;
            this.v0 = v0;
            if (solution != null)
            {
                /* uhm, FIXME: this round trip is silly */
                this.pv0 = rot * lambda;
                this.pr0 = rot * lambdaDot;
                this.tgo = tgo;
                this.tgo_bar = tgo / t_scale;
                this.vgo = vgo;
                this.vgo_bar = vgo / v_scale;
            }
            r0_bar = this.r0 / r_scale;
            v0_bar = this.v0 / v_scale;
        }

        public void centralForceThrust(double[] y, double x, double[] dy, object o)
        {
            Arc arc = (Arc)o;
            double At = arc.thrust / ( y[12] * g_bar );
            double r2 = y[0]*y[0] + y[1]*y[1] + y[2]*y[2];
            double r = Math.Sqrt(r2);
            double r3 = r2 * r;
            double r5 = r3 * r2;
            double pvm = Math.Sqrt(y[6]*y[6] + y[7]*y[7] + y[8]*y[8]);
            double rdotpv = y[0] * y[6] + y[1] * y[7] + y[2] * y[8];

            /* dr = v */
            dy[0] = y[3];
            dy[1] = y[4];
            dy[2] = y[5];
            /* dv = - r / r^3 + At * u */
            dy[3] = - y[0] / r3 + At * y[6] / pvm;
            dy[4] = - y[1] / r3 + At * y[7] / pvm;
            dy[5] = - y[2] / r3 + At * y[8] / pvm;
            /* dpv = - pr */
            dy[6] = - y[9];
            dy[7] = - y[10];
            dy[8] = - y[11];
            /* dpr = pv / r3 - 3 / r5 dot(r, pv) r */
            dy[9]  = y[6] / r3 - 3 / r5 * rdotpv * y[0];
            dy[10] = y[7] / r3 - 3 / r5 * rdotpv * y[1];
            dy[11] = y[8] / r3 - 3 / r5 * rdotpv * y[2];
            /* m = mdot */
            dy[12] = ( arc.thrust == 0 ) ? 0 : - arc.thrust / arc.c;
            /* accumulated ∆v of the arc */
            dy[13] = At;
        }

        /*
        public void terminal5constraint(Vector3d rT, Vector3d vT)
        {
            bcfun = (double[] yT, double[] zterm) => terminal5constraint(yT, zterm, rT, vT);
        }

        private void terminal5constraint(double[] yT, double[] z, Vector3d rT, Vector3d vT)
        {
            QuaternionD rot = Quaternion.Inverse(Planetarium.fetch.rotation);
            Vector3d rT_bar = rot * rT / r_scale;
            Vector3d vT_bar = rot * vT / v_scale;

            Vector3d rf = new Vector3d(yT[0], yT[1], yT[2]);
            Vector3d vf = new Vector3d(yT[3], yT[4], yT[5]);
            Vector3d pvf = new Vector3d(yT[6], yT[7], yT[8]);
            Vector3d prf = new Vector3d(yT[9], yT[10], yT[11]);

            Vector3d hT = Vector3d.Cross(rT_bar, vT_bar);

            if (hT[1] == 0)
            {
                rf = rf.Reorder(231);
                vf = vf.Reorder(231);
                rT_bar = rT_bar.Reorder(231);
                vT_bar = vT_bar.Reorder(231);
                prf = prf.Reorder(231);
                pvf = pvf.Reorder(231);
                hT = Vector3d.Cross(rT_bar, vT_bar);
            }

            Vector3d hf = Vector3d.Cross(rf, vf);
            Vector3d eT = - ( rT_bar.normalized + Vector3d.Cross(hT, vT_bar) );
            Vector3d ef = - ( rf.normalized + Vector3d.Cross(hf, vf) );
            Vector3d hmiss = hf - hT;
            Vector3d emiss = ef - eT;
            double trans = Vector3d.Dot(prf, vf) - Vector3d.Dot(pvf, rf) / ( rf.magnitude * rf.magnitude * rf.magnitude );

            z[0] = hmiss[0];
            z[1] = hmiss[1];
            z[2] = hmiss[2];
            z[3] = emiss[0];
            z[4] = emiss[2];
            z[5] = trans;
        }
        */

        double ckEps = 1e-7;

        /* used to update y0 to yf without intermediate values */
        public void singleIntegrate(double[] y0, double[] yf, int n, ref double t, double dt, Arc e, ref double dV)
        {
            singleIntegrate(y0, yf, null, n, ref t, dt, e, 2, ref dV);
        }

        /* used to pull intermediate values off to do chebyshev polynomial interpolation */
        public void singleIntegrate(double[] y0, Solution sol, int n, ref double t, double dt, Arc e, int count, ref double dV)
        {
            singleIntegrate(y0, null, sol, n, ref t, dt, e, count, ref dV);
        }

        public abstract int arcIndex { get; }

        public void singleIntegrate(double[] y0, double[] yf, Solution sol, int n, ref double t, double dt, Arc e, int count, ref double dV)
        {
            if ( count < 2)
                count = 2;

            /* clip negative thrusting burns to zero */
            if ( dt < 0 && e.thrust != 0)
                Debug.Log("WARNING: negative time thrusting burn!!!!!");
            //    dt = 0;

            if ( dt == 0 && yf != null)
            {
                Array.Copy(y0, 13*n+arcIndex, yf, 13*n, 13);
                return;
            }

            double[] y = new double[14];
            Array.Copy(y0, 13*n+arcIndex, y, 0, 13);
            y[13] = dV;

            /* time to burn the entire stage */
            double tau = e.isp * g0 * y[12] / e.thrust / t_scale;

            /* clip the integration so we don't burn the whole rocket (causes infinite spinning) */
            if ( dt > 0.999 * tau )
            {
                Debug.Log("WARNING: TAU CLIPPING!!! " + dt / tau);
                dt = 0.999 * tau;  /* don't add any more 9's or we get infinite loops */
            }


            double[] x = new double[count];
            // Chebyshev sampling
            for(int k = 1; k < (count-1); k++)
            {
                int l = count - 2;
                x[k] = t + 0.5 * dt  + 0.5 * dt * Math.Cos(Math.PI*(2*(l-k)+1)/(2*l));
            }

            // But also get the endpoints exactly
            x[0] = t;
            x[count-1] = t + dt;

            double[] xtbl;
            double[,] ytbl;
            int m;
            alglib.odesolverstate s;
            alglib.odesolverreport rep;
            alglib.odesolverrkck(y, 14, x, count, ckEps, 0, out s);
            alglib.odesolversolve(s, centralForceThrust, e);
            alglib.odesolverresults(s, out m, out xtbl, out ytbl, out rep);
            t = t + dt;

            e.dV = ( ytbl[count-1,13] - dV ) * v_scale;
            dV = ytbl[count-1,13];

            if (sol != null && dt != 0)
            {
                sol.AddSegment(xtbl, ytbl, e);
            }

            if (yf != null)
            {
                for(int i = 0; i < 13; i++)
                {
                    int j = 13*n+i;
                    yf[j] = ytbl[1,i];
                }
            }
        }

        public void multipleIntegrate(double[] y0, double[] yf, List<Arc> arcs)
        {
            multipleIntegrate(y0, yf, null, arcs);
        }

        public void multipleIntegrate(double[] y0, Solution sol, List<Arc> arcs, int count)
        {
            multipleIntegrate(y0, null, sol, arcs, count);
        }

        public void multipleIntegrate(double[] y0, double[] yf, Solution sol, List<Arc> arcs, int count = 2)
        {
            double t = 0;

            double tgo = y0[0];
            double dV = 0;

            for(int i = 0; i < arcs.Count; i++)
            {
                if ( (tgo <= arcs[i].max_bt_bar) || (i == arcs.Count-1) )
                {
                    if (yf != null) {
                        singleIntegrate(y0, yf, i, ref t, tgo, arcs[i], ref dV);
                    } else {
                        singleIntegrate(y0, sol, i, ref t, tgo, arcs[i], count, ref dV);
                    }
                    tgo = 0;
                }
                else
                {
                    if (yf != null) {
                        singleIntegrate(y0, yf, i, ref t, arcs[i].max_bt_bar, arcs[i], ref dV);
                    } else {
                        singleIntegrate(y0, sol, i, ref t, arcs[i].max_bt_bar, arcs[i], count, ref dV);
                    }
                    tgo -= arcs[i].max_bt_bar;
                }
            }
        }

        public double[] yf;

        public virtual void optimizationFunction(double[] y0, double[] z, object o)
        {
            List<Arc> arcs = (List<Arc>)o;
            yf = new double[arcs.Count*13];  /* somewhat confusingly y0 contains the state, costate and parameters, while yf omits the parameters */
            multipleIntegrate(y0, yf, arcs);

            /* initial conditions */
            z[0] = y0[arcIndex+0] - r0_bar[0];
            z[1] = y0[arcIndex+1] - r0_bar[1];
            z[2] = y0[arcIndex+2] - r0_bar[2];
            z[3] = y0[arcIndex+3] - v0_bar[0];
            z[4] = y0[arcIndex+4] - v0_bar[1];
            z[5] = y0[arcIndex+5] - v0_bar[2];
            z[6] = y0[arcIndex+12] - arcs[0].m0;

            /* terminal constraints */
            // FIXME: we could move the terminal constraints after the continuity conditions and
            // drop the bcfun indirection junk entirely
            double[] yT = new double[13];
            Array.Copy(yf, (arcs.Count-1)*13, yT, 0, 13);
            double[] zterm = new double[6];
            if ( bcfun == null )
                throw new Exception("No bcfun was provided to the Pontryagin optimizer");

            bcfun(yT, zterm);

            z[7] = zterm[0];
            z[8] = zterm[1];
            z[9] = zterm[2];
            z[10] = zterm[3];
            z[11] = zterm[4];
            z[12] = zterm[5];

            /* multiple shooting continuity */
            for(int i = 1; i < arcs.Count; i++)
            {
                for(int j = 0; j < 13; j++)
                {
                    if ( j == 12 )
                    {
                        if (arcs[i].m0 < 0) // negative mass => continuity rather than mass jettison
                        {
                            /* continuity */
                            z[j+13*i] = y0[j+13*i] - yf[j+13*(i-1)];
                        }
                        else
                        {
                            /* mass jettison */
                            z[j+13*i] = y0[j+13*i+arcIndex] - arcs[i].m0;
                        }
                    }
                    else
                    {
                        z[j+13*i] = y0[j+13*i+arcIndex] - yf[j+13*(i-1)];
                    }
                }
            }

            /* NOTE SUBCLASSES SHOULD OVERRIDE THIS FUNCTION, CALL THIS BASE CLASS, ADD SWITCHING CONDITIONS, AND THEN SQUARE THE RESIDUALS */
        }

        double lmEpsx = 1e-9; // 1e-15;
        int lmIter = 20000;
        double lmDiffStep = 0.00001;

        public bool runOptimizer(List<Arc> arcs)
        {
            Debug.Log("arcs in runOptimizer:");
            for(int i = 0; i < arcs.Count; i++)
                Debug.Log(arcs[i]);

            for(int i = 0; i < y0.Length; i++)
                Debug.Log("runOptimizer before - y0[" + i + "] = " + y0[i]);

            double[] z = new double[13 * arcs.Count + arcIndex];
            optimizationFunction(y0, z, arcs);

            double znorm = 0.0;

            for(int i = 0; i < z.Length; i++)
            {
                znorm += z[i] * z[i];
                Debug.Log("zbefore[" + i + "] = " + z[i]);
            }

            znorm = Math.Sqrt(znorm);
            Debug.Log("znorm = " + znorm);

            alglib.minlmstate state;
            alglib.minlmreport rep = new alglib.minlmreport();
            alglib.minlmcreatev(y0.Length, y0, lmDiffStep, out state);  /* y0.Length must == z.Length returned by the BC function for square problems */
            alglib.minlmsetcond(state, lmEpsx, lmIter);
            alglib.minlmoptimize(state, optimizationFunction, null, arcs);

            double[] y0_new = new double[y0.Length];
            alglib.minlmresultsbuf(state, ref y0_new, rep);
            Debug.Log("MechJeb minlmoptimize termination code: " + rep.terminationtype);

            if (rep.terminationtype == 2)
                y0 = y0_new;

            optimizationFunction(y0, z, arcs);

            znorm = 0.0;

            for(int i = 0; i < z.Length; i++)
            {
                znorm += z[i] * z[i];
                Debug.Log("z[" + i + "] = " + z[i]);
            }

            znorm = Math.Sqrt(znorm);
            Debug.Log("znorm = " + znorm);

            if (znorm > 1e-5)
                return false;

            return (rep.terminationtype == 2) || (rep.terminationtype == 7);
        }

        public double[] y0;

        public void UpdateY0()
        {
            /* FIXME: some of the round tripping here is silly */
            Stage s = stages[0];
            y0[arcIndex] = r0_bar[0];
            y0[arcIndex+1] = r0_bar[1];
            y0[arcIndex+2] = r0_bar[2];
            y0[arcIndex+3] = v0_bar[0];
            y0[arcIndex+4] = v0_bar[1];
            y0[arcIndex+5] = v0_bar[2];
            y0[arcIndex+6] = pv0[0];
            y0[arcIndex+7] = pv0[1];
            y0[arcIndex+8] = pv0[2];
            y0[arcIndex+9] = pr0[0];
            y0[arcIndex+10] = pr0[1];
            y0[arcIndex+11] = pr0[2];
            y0[arcIndex+12] = s.m0;
            y0[0] = tgo_bar;
            y0[1] = 0;
        }

        public abstract void Optimize(double t0);

        public Solution solution;

        private Thread thread;
        private Mutex mut = new Mutex();

        /* very simple stage tracking for now */
        public virtual void SynchStages(List<int> kspstages, FuelFlowSimulation.Stats[] vacStats, double vesselMass)
        {
            if (thread != null && thread.IsAlive)
                return;

            bool first_thrusting = true;

            if (stages == null)
                stages = new List<Stage>();

            /* if the kspstages list shrank assume staging at position 0 */
            if (stages.Count > kspstages.Count)
            {
                for(int i = 0; i < (stages.Count - kspstages.Count); i++)
                {
                    Debug.Log("shrinking stage list by one");
                    stages[0].staged = true;
                    stages.RemoveAt(0);
                }
            }

            for(int stage_index = 0; stage_index < kspstages.Count; stage_index++)
            {
                int ksp_stage = kspstages[stage_index];

                double m0 = ( first_thrusting ? vesselMass : vacStats[ksp_stage].startMass ) * 1000;
                double isp = vacStats[ksp_stage].isp;
                double thrust = vacStats[ksp_stage].startThrust * 1000;
                double max_bt = vacStats[ksp_stage].deltaTime;

                if (stage_index >= stages.Count)
                {
                    Debug.Log("adding a new found stage");
                    stages.Add(new Stage(this, m0: m0, isp: isp, thrust: thrust, max_bt: max_bt, ksp_stage: ksp_stage));
                }
                else
                {
                    stages[stage_index].UpdateStage(this, m0: m0, isp: isp, thrust: thrust, max_bt: max_bt, ksp_stage: ksp_stage);
                }

                first_thrusting = false;
            }
        }

        public bool threadStart(double t0)
        {
            bool ret = false;

            if (mut.WaitOne(1000)) {
                if (thread != null && thread.IsAlive)
                {
                    ret = false;
                }
                else
                {
                    if (thread != null)
                        thread.Abort();

                    thread = new Thread(() => Optimize(t0));
                    thread.Start();
                    ret = true;
                }
                mut.ReleaseMutex();
            }
            return ret;
        }

        public void KillThread()
        {
            if (mut.WaitOne(1000)) {
                if (thread != null)
                    thread.Abort();
                mut.ReleaseMutex();
            }
        }
    }
}